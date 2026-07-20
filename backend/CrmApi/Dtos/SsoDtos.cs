namespace CrmApi.Dtos;

// ClientSecret is deliberately never echoed back — same "write-only secret"
// convention as ApiKey/WebhookSubscription. Updating a connection always
// requires re-entering the full config (no partial-update-preserving-secret
// logic), which keeps this simple at the cost of an admin having to paste
// the client secret again for any unrelated field change.
public record SsoConnectionDto(string Id, string Issuer, string ClientId, string EmailDomain, bool Enforced, bool IsActive, DateTime CreatedAt);

public record UpdateSsoConnectionRequest(string Issuer, string ClientId, string ClientSecret, string EmailDomain, bool Enforced, bool IsActive);

public record SsoStartRequest(string Email, string RedirectUri);

public record SsoStartResponse(bool Found, string? AuthorizationUrl, string? State);

public record SsoExchangeRequest(string State, string Code, string RedirectUri);
