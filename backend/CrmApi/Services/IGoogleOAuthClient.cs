namespace CrmApi.Services;

public record GoogleTokenResponse(string AccessToken, string IdToken, string TokenType, int ExpiresIn);

public record GoogleUserInfo(string Sub, string Email, bool EmailVerified, string? Name);

// Thin seam around Google's OAuth endpoints so tests can inject a fake —
// mirrors IAnthropicMessagesClient's rationale (qa-test-engineer: never call
// a real third-party API in tests).
public interface IGoogleOAuthClient
{
    Task<GoogleTokenResponse> ExchangeCodeAsync(string code);
    Task<GoogleUserInfo> GetUserInfoAsync(string accessToken);
}
