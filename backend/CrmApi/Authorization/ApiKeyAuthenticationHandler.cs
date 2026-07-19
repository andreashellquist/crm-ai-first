using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Encodings.Web;
using CrmApi.Data;
using CrmApi.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CrmApi.Authorization;

// Authenticates the public v1 API (see the public-api-and-webhooks skill) by
// an X-Api-Key header rather than the session JWT — a separate front door
// for external integrators, but it resolves to the exact same "workspaceId"
// claim CurrentUser reads for session auth, so every v1 controller re-scopes
// its queries by CurrentUser.WorkspaceId exactly like the rest of the app.
// There is no separate, less-checked tenant-isolation path for API-key auth.
public class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    AppDbContext db) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";
    private const string HeaderName = "X-Api-Key";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var headerValues))
            return AuthenticateResult.Fail($"Missing {HeaderName} header");

        var rawKey = headerValues.ToString();
        if (string.IsNullOrWhiteSpace(rawKey))
            return AuthenticateResult.Fail($"Missing {HeaderName} header");

        var hashed = ApiKeyGenerator.Hash(rawKey);
        var apiKey = await db.ApiKeys.FirstOrDefaultAsync(k => k.HashedKey == hashed);
        if (apiKey is null || apiKey.RevokedAt is not null)
            return AuthenticateResult.Fail("Invalid or revoked API key");

        apiKey.LastUsedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, apiKey.CreatedByUserId),
            new("workspaceId", apiKey.WorkspaceId),
            new("apiKeyId", apiKey.Id),
        };
        claims.AddRange(apiKey.Scopes.Select(scope => new Claim("scope", scope)));

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return AuthenticateResult.Success(ticket);
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Response.WriteAsJsonAsync(new { error = "Invalid or missing API key" });
    }
}
