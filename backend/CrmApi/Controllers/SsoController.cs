using CrmApi.Authorization;
using CrmApi.Data;
using CrmApi.Dtos;
using CrmApi.Models;
using CrmApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmApi.Controllers;

// Per-workspace SSO (OIDC) — see auth-security-expert's "Enterprise auth"
// section and Models/SsoConnection.cs. Config endpoints (session-JWT-
// authenticated, under /api/workspace/sso) manage a workspace's own
// connection; the sign-in endpoints (/api/auth/sso/*) are deliberately
// anonymous — a user hasn't authenticated with anything yet when they're
// starting an SSO flow, same reasoning as AuthController.Login/
// GoogleExchange.
[ApiController]
public class SsoController(AppDbContext db, JwtService jwt, CurrentUser current, AuditLogService audit, ISecretProtector secretProtector, ILogger<SsoController> logger) : ControllerBase
{
    [HttpGet("api/workspace/sso")]
    [Authorize]
    public async Task<ActionResult<SsoConnectionDto?>> Get()
    {
        var connection = await db.SsoConnections.FirstOrDefaultAsync(c => c.WorkspaceId == current.WorkspaceId);
        return Ok(connection is null ? null : ToDto(connection));
    }

    [HttpPut("api/workspace/sso")]
    [Authorize]
    [RequireRole("owner", "admin")]
    public async Task<ActionResult<SsoConnectionDto>> Update(UpdateSsoConnectionRequest request)
    {
        if (!Uri.TryCreate(request.Issuer, UriKind.Absolute, out var issuerUri) || issuerUri.Scheme != "https")
            return BadRequest("Issuer must be an absolute https:// URL");
        if (string.IsNullOrWhiteSpace(request.ClientId)) return BadRequest("Client ID is required");
        if (string.IsNullOrWhiteSpace(request.ClientSecret)) return BadRequest("Client secret is required");

        var domain = request.EmailDomain.Trim().TrimStart('@').ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(domain) || !domain.Contains('.') || domain.Contains('@'))
            return BadRequest("Email domain must look like \"acme.com\", not a full email address");

        var domainTaken = await db.SsoConnections.AnyAsync(c => c.EmailDomain == domain && c.WorkspaceId != current.WorkspaceId);
        if (domainTaken) return Conflict($"\"{domain}\" is already connected to a different workspace");

        var connection = await db.SsoConnections.FirstOrDefaultAsync(c => c.WorkspaceId == current.WorkspaceId);
        var encryptedSecret = secretProtector.Protect(request.ClientSecret);
        if (connection is null)
        {
            connection = new SsoConnection
            {
                WorkspaceId = current.WorkspaceId,
                Issuer = request.Issuer,
                ClientId = request.ClientId,
                ClientSecret = encryptedSecret,
                EmailDomain = domain,
            };
            db.SsoConnections.Add(connection);
        }
        else
        {
            connection.Issuer = request.Issuer;
            connection.ClientId = request.ClientId;
            connection.ClientSecret = encryptedSecret;
            connection.EmailDomain = domain;
        }
        connection.Enforced = request.Enforced;
        connection.IsActive = request.IsActive;
        connection.UpdatedAt = DateTime.UtcNow;
        audit.Log(current.WorkspaceId, current.UserId, AuditLogService.Actions.SsoConnectionUpdated, "SsoConnection", connection.Id,
            new { emailDomain = connection.EmailDomain, enforced = connection.Enforced, isActive = connection.IsActive });
        await db.SaveChangesAsync();

        return Ok(ToDto(connection));
    }

    [HttpDelete("api/workspace/sso")]
    [Authorize]
    [RequireRole("owner", "admin")]
    public async Task<IActionResult> Delete()
    {
        var connection = await db.SsoConnections.FirstOrDefaultAsync(c => c.WorkspaceId == current.WorkspaceId);
        if (connection is null) return NotFound();

        audit.Log(current.WorkspaceId, current.UserId, AuditLogService.Actions.SsoConnectionRemoved, "SsoConnection", connection.Id,
            new { emailDomain = connection.EmailDomain });
        db.SsoConnections.Remove(connection);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // Anonymous — routes a bare email to whichever workspace's connection
    // claims its domain, before the user has authenticated with anything.
    // Returns Found=false (not an error) for a domain with no connection,
    // matching this app's "let the caller decide, don't leak which domains
    // exist via a 404 vs 200 distinction" — a 200/false either way avoids
    // that domain-enumeration signal being any more informative via status
    // code than via body.
    [HttpPost("api/auth/sso/start")]
    public async Task<ActionResult<SsoStartResponse>> Start(SsoStartRequest request, [FromServices] IOidcClient oidc)
    {
        var domain = request.Email.Split('@', 2).ElementAtOrDefault(1)?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(domain)) return Ok(new SsoStartResponse(false, null, null));

        var connection = await db.SsoConnections.FirstOrDefaultAsync(c => c.EmailDomain == domain && c.IsActive);
        if (connection is null) return Ok(new SsoStartResponse(false, null, null));

        OidcDiscoveryDocument discovery;
        try
        {
            discovery = await oidc.DiscoverAsync(connection.Issuer);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "sso start discovery_failed workspaceId={WorkspaceId}", connection.WorkspaceId);
            return Ok(new SsoStartResponse(false, null, null));
        }

        // Carries the connection id across the IdP redirect round-trip —
        // the nonce is what the frontend's CSRF cookie check guards, not
        // this value itself (the connection id is not a secret; it's
        // equivalent to any other resource id this app hands out).
        var state = $"{connection.Id}.{Guid.NewGuid():N}";

        var queryParams = new Dictionary<string, string>
        {
            ["client_id"] = connection.ClientId,
            ["redirect_uri"] = request.RedirectUri,
            ["response_type"] = "code",
            ["scope"] = "openid email profile",
            ["state"] = state,
        };
        var queryString = string.Join("&", queryParams.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        var separator = discovery.AuthorizationEndpoint.Contains('?') ? "&" : "?";
        var authorizationUrl = $"{discovery.AuthorizationEndpoint}{separator}{queryString}";

        return Ok(new SsoStartResponse(true, authorizationUrl, state));
    }

    // Anonymous — see AuthController.GoogleExchange for the identical
    // "browser never talks to the .NET API directly" rationale: the
    // frontend's own callback route forwards the code here server-to-server.
    [HttpPost("api/auth/sso/exchange")]
    public async Task<ActionResult<LoginResponse>> Exchange(SsoExchangeRequest request, [FromServices] IOidcClient oidc)
    {
        var connectionId = request.State.Split('.', 2).ElementAtOrDefault(0);
        if (string.IsNullOrEmpty(connectionId)) return Unauthorized("Invalid SSO state");

        var connection = await db.SsoConnections.Include(c => c.Workspace)
            .FirstOrDefaultAsync(c => c.Id == connectionId && c.IsActive);
        if (connection?.Workspace is null) return Unauthorized("SSO connection no longer exists");

        OidcUserInfo userInfo;
        try
        {
            var discovery = await oidc.DiscoverAsync(connection.Issuer);
            var clientSecret = secretProtector.Unprotect(connection.ClientSecret);
            var tokens = await oidc.ExchangeCodeAsync(discovery, connection.ClientId, clientSecret, request.Code, request.RedirectUri);
            userInfo = await oidc.GetUserInfoAsync(discovery, tokens.AccessToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "sso exchange_failed workspaceId={WorkspaceId}", connection.WorkspaceId);
            return Unauthorized("SSO sign-in failed");
        }

        if (!userInfo.EmailVerified) return Unauthorized("SSO account email is not verified");

        var emailDomain = userInfo.Email.Split('@', 2).ElementAtOrDefault(1)?.ToLowerInvariant();
        if (emailDomain != connection.EmailDomain)
            return Unauthorized("SSO account email does not match this connection's domain");

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == userInfo.Email);
        if (user is null)
        {
            // Same "unguessable random password hash" trick as GoogleExchange
            // — PasswordHash is required and must be a real bcrypt hash, but
            // password login must never succeed for an SSO-only user.
            user = new User
            {
                Email = userInfo.Email,
                Name = userInfo.Name,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString("N")),
            };
            db.Users.Add(user);
        }

        // Just-in-time provisioning into *this* connection's workspace —
        // unlike GoogleExchange, SSO never creates a new workspace: the
        // workspace already exists (an admin configured the connection on
        // it), so a first-time SSO sign-in joins it rather than spinning up
        // a fresh one.
        var membership = await db.WorkspaceMembers
            .FirstOrDefaultAsync(m => m.UserId == user.Id && m.WorkspaceId == connection.WorkspaceId);
        if (membership is null)
        {
            membership = new WorkspaceMember { WorkspaceId = connection.WorkspaceId, UserId = user.Id, Role = "member" };
            db.WorkspaceMembers.Add(membership);
            logger.LogInformation("sso jit_provisioned userId={UserId} workspaceId={WorkspaceId}", user.Id, connection.WorkspaceId);
        }
        else if (!membership.IsActive)
        {
            return Unauthorized();
        }

        await db.SaveChangesAsync();

        var token = jwt.GenerateToken(user.Id, connection.WorkspaceId, membership.Role);
        return Ok(new LoginResponse(token, connection.WorkspaceId, connection.Workspace.Name));
    }

    private static SsoConnectionDto ToDto(SsoConnection c) =>
        new(c.Id, c.Issuer, c.ClientId, c.EmailDomain, c.Enforced, c.IsActive, c.CreatedAt);
}
