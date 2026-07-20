namespace CrmApi.Models;

// Per-workspace SSO (OIDC) configuration — an admin connects their IdP
// (Okta, Azure AD, Google Workspace, etc.) rather than this being a global
// app setting. See auth-security-expert's "Enterprise auth" section:
// SCAFFOLD ONLY, same reasoning as GoogleOAuthClient — this agent has no
// real IdP to register a client against, so the flow is fully built and
// tested against a fake, but unusable until a real workspace admin
// configures a real Issuer/ClientId/ClientSecret.
public class SsoConnection
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string WorkspaceId { get; set; }

    // OIDC issuer base URL, e.g. "https://acme.okta.com" — discovery is
    // fetched live from "{Issuer}/.well-known/openid-configuration" at
    // sign-in time rather than caching endpoint URLs on this row, so a
    // provider rotating its endpoints doesn't require this app to notice.
    public required string Issuer { get; set; }
    public required string ClientId { get; set; }

    // Plaintext at rest, matching this codebase's existing
    // WebhookSubscription.Secret precedent — see auth-security-expert's
    // "encrypt third-party credentials at rest" guidance, which nothing in
    // this app implements yet. A real, tracked follow-up shared with that
    // webhook secret, not something unique to SSO.
    public required string ClientSecret { get; set; }

    // Routes a bare email address (entered on the SSO sign-in page, before
    // the user has authenticated with anything) to this connection — e.g.
    // "acme.com" matches "jane@acme.com". Lowercased, no leading "@".
    public required string EmailDomain { get; set; }

    // When true, password login is rejected for any user whose email
    // matches EmailDomain — see auth-security-expert: "default to admin can
    // enforce it rather than silently disabling other login methods,"
    // i.e. this must be explicitly turned on, never implied by a connection
    // merely existing.
    public bool Enforced { get; set; }
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Workspace? Workspace { get; set; }
}
