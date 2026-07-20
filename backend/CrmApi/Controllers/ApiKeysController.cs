using CrmApi.Authorization;
using CrmApi.Data;
using CrmApi.Dtos;
using CrmApi.Models;
using CrmApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmApi.Controllers;

// Session-JWT-authenticated management of the public v1 API's credentials —
// see public-api-and-webhooks skill. Issuing a credential that grants
// external access to workspace data is restricted to owner/admin, same as
// any other workspace-configuration write (auth-security-expert).
[ApiController]
[Route("api/api-keys")]
[Authorize]
public class ApiKeysController(AppDbContext db, CurrentUser current, AuditLogService audit) : ControllerBase
{
    public static readonly string[] ValidScopes =
    [
        "contacts:read", "companies:read", "deals:read", "field-definitions:read",
        // Deliberately reuses the same credential/scoping infrastructure as
        // customer-integration keys rather than a parallel secret system —
        // a SCIM token is, mechanically, just a bearer credential scoped to
        // one job (see ScimUsersController / the public-api-and-webhooks
        // and auth-security-expert skills).
        "scim:users",
    ];

    [HttpGet]
    public async Task<ActionResult<List<ApiKeyDto>>> List()
    {
        var keys = await db.ApiKeys
            .Where(k => k.WorkspaceId == current.WorkspaceId)
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync();
        return Ok(keys.Select(ToDto).ToList());
    }

    [HttpPost]
    [RequireRole("owner", "admin")]
    public async Task<ActionResult<CreateApiKeyResponse>> Create(CreateApiKeyRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest("Name is required");
        var scopes = (request.Scopes ?? []).Distinct().ToList();
        if (scopes.Count == 0) return BadRequest("At least one scope is required");
        var invalidScopes = scopes.Except(ValidScopes).ToList();
        if (invalidScopes.Count > 0) return BadRequest($"Unknown scope(s): {string.Join(", ", invalidScopes)}");

        var (rawKey, hashedKey) = ApiKeyGenerator.Generate();
        var apiKey = new ApiKey
        {
            WorkspaceId = current.WorkspaceId,
            Name = request.Name,
            HashedKey = hashedKey,
            Scopes = scopes,
            CreatedByUserId = current.UserId,
        };
        db.ApiKeys.Add(apiKey);
        audit.Log(current.WorkspaceId, current.UserId, AuditLogService.Actions.ApiKeyCreated, "ApiKey", apiKey.Id,
            new { name = apiKey.Name, scopes = apiKey.Scopes });
        await db.SaveChangesAsync();

        return Ok(new CreateApiKeyResponse(ToDto(apiKey), rawKey));
    }

    [HttpDelete("{id}")]
    [RequireRole("owner", "admin")]
    public async Task<IActionResult> Revoke(string id)
    {
        var apiKey = await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id && k.WorkspaceId == current.WorkspaceId);
        if (apiKey is null) return NotFound();

        apiKey.RevokedAt ??= DateTime.UtcNow;
        audit.Log(current.WorkspaceId, current.UserId, AuditLogService.Actions.ApiKeyRevoked, "ApiKey", apiKey.Id, new { name = apiKey.Name });
        await db.SaveChangesAsync();
        return NoContent();
    }

    private static ApiKeyDto ToDto(ApiKey k) => new(k.Id, k.Name, k.Scopes, k.LastUsedAt, k.RevokedAt, k.CreatedAt);
}
