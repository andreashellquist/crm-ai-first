using System.Text.Json;
using System.Text.RegularExpressions;
using CrmApi.Authorization;
using CrmApi.Data;
using CrmApi.Dtos;
using CrmApi.Models;
using CrmApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace CrmApi.Controllers;

// A minimal, real RFC 7644 SCIM 2.0 /Users implementation for enterprise
// IdP-driven provisioning (Okta, Azure AD, etc.) — see auth-security-expert.
// Authenticated the same way the public v1 API is (X-Api-Key +
// RequireScope("scim:users")), deliberately reusing that credential/scoping
// infrastructure rather than a parallel secret system.
//
// The SCIM resource id is WorkspaceMember.Id, not User.Id — SCIM here
// models "this person's presence in this workspace," matching
// WorkspaceMember.IsActive's per-workspace (not whole-account) deactivation
// semantics.
[ApiController]
[Route("api/scim/v2/Users")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationHandler.SchemeName)]
[RequireScope("scim:users")]
[EnableRateLimiting("ApiKey")]
public class ScimUsersController(AppDbContext db, CurrentUser current) : ControllerBase
{
    private const int DefaultCount = 100;
    private const int MaxCount = 200;
    private static readonly Regex UserNameEqFilter = new("^userName eq \"(.+)\"$", RegexOptions.IgnoreCase);

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? filter, [FromQuery] int startIndex = 1, [FromQuery] int count = DefaultCount)
    {
        startIndex = Math.Max(startIndex, 1);
        count = Math.Clamp(count, 1, MaxCount);

        var query = db.WorkspaceMembers.Include(m => m.User)
            .Where(m => m.WorkspaceId == current.WorkspaceId);

        if (!string.IsNullOrWhiteSpace(filter))
        {
            var match = UserNameEqFilter.Match(filter);
            if (!match.Success)
            {
                return BadRequest(Error("400", "Only the \"userName eq \\\"value\\\"\" filter is supported", "invalidFilter"));
            }
            var userName = match.Groups[1].Value;
            query = query.Where(m => m.User!.Email == userName);
        }

        var total = await query.CountAsync();
        var page = await query.OrderBy(m => m.CreatedAt)
            .Skip(startIndex - 1)
            .Take(count)
            .ToListAsync();

        return Ok(new ScimListResponse(
            ["urn:ietf:params:scim:api:messages:2.0:ListResponse"],
            total, startIndex, page.Count,
            page.Select(ToDto).ToList()
        ));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id)
    {
        var member = await FindAsync(id);
        if (member is null) return ScimNotFound(id);
        return Ok(ToDto(member));
    }

    [HttpPost]
    public async Task<IActionResult> Create(ScimCreateUserRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.UserName))
            return BadRequest(Error("400", "userName is required", "invalidValue"));

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == request.UserName);
        if (user is not null)
        {
            var alreadyMember = await db.WorkspaceMembers
                .AnyAsync(m => m.UserId == user.Id && m.WorkspaceId == current.WorkspaceId);
            if (alreadyMember)
                return Conflict(Error("409", $"userName \"{request.UserName}\" already exists in this workspace", "uniqueness"));
        }
        else
        {
            user = new User
            {
                Email = request.UserName,
                Name = request.Name?.GivenName,
                // SCIM-provisioned users authenticate via their IdP's SSO,
                // never this app's password login — same "real bcrypt hash
                // of an unguessable value" pattern as OAuth-provisioned
                // users (AuthController.GoogleExchange).
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString("N")),
            };
            db.Users.Add(user);
        }

        var member = new WorkspaceMember
        {
            WorkspaceId = current.WorkspaceId,
            UserId = user.Id,
            // v1 scope: every SCIM-provisioned member lands as "member" —
            // mapping IdP groups/roles to workspace roles is a deliberate
            // follow-up, not a hidden gap.
            Role = "member",
            IsActive = request.Active ?? true,
        };
        db.WorkspaceMembers.Add(member);
        await db.SaveChangesAsync();
        member.User = user;

        return CreatedAtAction(nameof(Get), new { id = member.Id }, ToDto(member));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Replace(string id, ScimReplaceUserRequest request)
    {
        var member = await FindAsync(id);
        if (member is null) return ScimNotFound(id);

        if (request.Name?.GivenName is not null) member.User!.Name = request.Name.GivenName;
        if (request.Active is not null) member.IsActive = request.Active.Value;
        member.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(ToDto(member));
    }

    // Real IdPs (Okta, Azure AD) deprovision via PATCH {"op":"replace",
    // "path":"active","value":false} rather than DELETE — this is the path
    // that actually matters for enterprise buyers. v1 supports only that
    // one operation, returning a spec-shaped error for anything else rather
    // than silently no-op'ing.
    [HttpPatch("{id}")]
    public async Task<IActionResult> Patch(string id, ScimPatchRequest request)
    {
        var member = await FindAsync(id);
        if (member is null) return ScimNotFound(id);

        foreach (var op in request.Operations ?? [])
        {
            if (!string.Equals(op.Op, "replace", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(op.Path, "active", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(Error("400",
                    "Only {\"op\":\"replace\",\"path\":\"active\"} is supported in this version", "invalidPath"));
            }
            if (op.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return BadRequest(Error("400", "\"active\" must be a boolean", "invalidValue"));

            member.IsActive = op.Value.GetBoolean();
        }
        member.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(ToDto(member));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var member = await db.WorkspaceMembers.FirstOrDefaultAsync(m => m.Id == id && m.WorkspaceId == current.WorkspaceId);
        if (member is null) return ScimNotFound(id);

        db.WorkspaceMembers.Remove(member);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private Task<WorkspaceMember?> FindAsync(string id) =>
        db.WorkspaceMembers.Include(m => m.User)
            .FirstOrDefaultAsync(m => m.Id == id && m.WorkspaceId == current.WorkspaceId);

    private NotFoundObjectResult ScimNotFound(string id) =>
        NotFound(Error("404", $"User {id} not found"));

    private static ScimError Error(string status, string detail, string? scimType = null) =>
        new(["urn:ietf:params:scim:api:messages:2.0:Error"], status, detail, scimType);

    private static ScimUserDto ToDto(WorkspaceMember member) => new(
        ["urn:ietf:params:scim:schemas:core:2.0:User"],
        member.Id,
        member.User!.Email,
        new ScimName(member.User.Name, null),
        [new ScimEmail(member.User.Email, true)],
        member.IsActive,
        new ScimMeta("User", member.CreatedAt, member.UpdatedAt)
    );
}
