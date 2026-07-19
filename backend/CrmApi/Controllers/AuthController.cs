using CrmApi.Data;
using CrmApi.Dtos;
using CrmApi.Models;
using CrmApi.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmApi.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(AppDbContext db, JwtService jwt, ILogger<AuthController> logger) : ControllerBase
{
    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == request.Email);
        if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return Unauthorized();

        var membership = await FirstMembershipAsync(user.Id);
        if (membership?.Workspace is null) return Unauthorized();

        var token = jwt.GenerateToken(user.Id, membership.WorkspaceId, membership.Role);
        return Ok(new LoginResponse(token, membership.WorkspaceId, membership.Workspace.Name));
    }

    // Public — read before a user has an account, so the signup page can
    // render a template picker. No PII, just static in-repo product content.
    [HttpGet("templates")]
    public ActionResult<List<VerticalTemplateSummaryDto>> Templates() =>
        Ok(VerticalTemplates.All.Select(ToTemplateDto).ToList());

    [HttpPost("register")]
    public async Task<ActionResult<LoginResponse>> Register(RegisterRequest request, [FromServices] WorkspaceProvisioningService provisioning)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest("Email and password are required");
        if (request.Password.Length < 8)
            return BadRequest("Password must be at least 8 characters");
        if (string.IsNullOrWhiteSpace(request.WorkspaceName))
            return BadRequest("Workspace name is required");
        if (VerticalTemplates.Find(request.TemplateId) is null)
            return BadRequest("Unknown starter template");

        if (await db.Users.AnyAsync(u => u.Email == request.Email))
            return Conflict("An account with this email already exists");

        var user = new User
        {
            Email = request.Email,
            Name = request.Name,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
        };
        db.Users.Add(user);

        var workspace = new Workspace { Name = request.WorkspaceName };
        db.Workspaces.Add(workspace);
        db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = workspace.Id, UserId = user.Id, Role = "owner" });
        await db.SaveChangesAsync();

        await provisioning.ProvisionAsync(workspace.Id, request.TemplateId);

        logger.LogInformation("auth register provisioned_new_workspace userId={UserId} workspaceId={WorkspaceId} template={TemplateId}",
            user.Id, workspace.Id, request.TemplateId);

        var token = jwt.GenerateToken(user.Id, workspace.Id, "owner");
        return Ok(new LoginResponse(token, workspace.Id, workspace.Name));
    }

    // SCAFFOLD ONLY — see GoogleOAuthClient. Called by the Next.js OAuth
    // callback route (server-to-server), never the browser directly: the
    // browser's redirect dance with Google terminates at the frontend, which
    // forwards the authorization code here to exchange it, preserving "the
    // browser never talks to the .NET API directly" (auth-security-expert).
    [HttpPost("google/exchange")]
    public async Task<ActionResult<LoginResponse>> GoogleExchange(
        GoogleExchangeRequest request, [FromServices] IGoogleOAuthClient google, [FromServices] WorkspaceProvisioningService provisioning)
    {
        GoogleUserInfo userInfo;
        try
        {
            var tokens = await google.ExchangeCodeAsync(request.Code);
            userInfo = await google.GetUserInfoAsync(tokens.AccessToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "oauth_google exchange_failed");
            return Unauthorized("Google sign-in failed");
        }

        if (!userInfo.EmailVerified) return Unauthorized("Google account email is not verified");

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == userInfo.Email);
        if (user is null)
        {
            // First-time OAuth sign-in — provision a user + a fresh workspace,
            // mirroring typical self-serve signup (no invite flow yet). The
            // password hash is a real bcrypt hash of an unguessable random
            // value: PasswordHash is required and BCrypt.Verify throws on a
            // non-bcrypt-format string, so this keeps the column valid while
            // guaranteeing password login can never succeed for this user.
            user = new User
            {
                Email = userInfo.Email,
                Name = userInfo.Name,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString("N")),
            };
            db.Users.Add(user);

            var workspaceName = string.IsNullOrWhiteSpace(userInfo.Name) ? $"{userInfo.Email}'s Workspace" : $"{userInfo.Name}'s Workspace";
            var workspace = new Workspace { Name = workspaceName };
            db.Workspaces.Add(workspace);
            db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = workspace.Id, UserId = user.Id, Role = "owner" });
            await db.SaveChangesAsync();

            // The OAuth flow has no template-picker step, unlike Register —
            // default to the generic template rather than leaving the
            // workspace with no Pipeline at all (GetBoard's IsDefault lookup
            // would otherwise find nothing for a first-time Google sign-in).
            await provisioning.ProvisionAsync(workspace.Id, VerticalTemplates.DefaultId);

            logger.LogInformation("oauth_google provisioned_new_user userId={UserId} workspaceId={WorkspaceId}", user.Id, workspace.Id);
        }

        var membership = await FirstMembershipAsync(user.Id);
        if (membership?.Workspace is null) return Unauthorized();

        var token = jwt.GenerateToken(user.Id, membership.WorkspaceId, membership.Role);
        return Ok(new LoginResponse(token, membership.WorkspaceId, membership.Workspace.Name));
    }

    private static VerticalTemplateSummaryDto ToTemplateDto(VerticalTemplate template)
    {
        string Term(string key, string fallback, bool plural = false)
        {
            if (!template.Terminology.TryGetValue(key, out var term)) return fallback;
            return (plural ? term.Plural : term.Singular) ?? term.Label ?? fallback;
        }

        return new VerticalTemplateSummaryDto(
            template.Id,
            template.Name,
            template.Description,
            Term("deal", "Deal"),
            Term("deal", "Deals", plural: true),
            Term("company", "Company"),
            Term("contact", "Contact"),
            template.Pipeline.Stages.Select(s => s.Name).ToList()
        );
    }

    // First membership only — no workspace-switching UI yet, matches the
    // Next.js walking skeleton's requireWorkspace() behavior.
    // Excludes SCIM-deactivated memberships (WorkspaceMember.IsActive) — a
    // deactivated member can't sign in via any path (password or Google),
    // same as having no membership at all. Doesn't revoke an
    // already-issued JWT (see ScimUsersController / auth-security-expert).
    private Task<WorkspaceMember?> FirstMembershipAsync(string userId) =>
        db.WorkspaceMembers
            .Include(m => m.Workspace)
            .Where(m => m.UserId == userId && m.IsActive)
            .OrderBy(m => m.CreatedAt)
            .FirstOrDefaultAsync();
}
