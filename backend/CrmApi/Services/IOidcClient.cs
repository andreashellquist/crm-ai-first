namespace CrmApi.Services;

public record OidcDiscoveryDocument(string AuthorizationEndpoint, string TokenEndpoint, string UserinfoEndpoint);

public record OidcTokenResponse(string AccessToken, string? IdToken, string TokenType, int ExpiresIn);

public record OidcUserInfo(string Sub, string Email, bool EmailVerified, string? Name);

// Thin seam around a generic OIDC provider's endpoints so tests can inject a
// fake — mirrors IGoogleOAuthClient/IAnthropicMessagesClient's rationale
// (qa-test-engineer: never call a real third-party API in tests). Unlike
// Google (one fixed provider, fixed endpoints), a workspace's connection
// names its own Issuer, so the endpoints themselves are discovered live per
// call rather than hardcoded.
public interface IOidcClient
{
    Task<OidcDiscoveryDocument> DiscoverAsync(string issuer);
    Task<OidcTokenResponse> ExchangeCodeAsync(OidcDiscoveryDocument discovery, string clientId, string clientSecret, string code, string redirectUri);
    Task<OidcUserInfo> GetUserInfoAsync(OidcDiscoveryDocument discovery, string accessToken);
}
