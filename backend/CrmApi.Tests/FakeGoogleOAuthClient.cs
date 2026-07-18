using CrmApi.Services;

namespace CrmApi.Tests;

// Stands in for the real Google OAuth client in every test — see
// qa-test-engineer: "never call a real third-party API in tests." Defaults
// to a canned verified user; tests override NextUserInfo/NextException
// before calling the exchange endpoint.
public class FakeGoogleOAuthClient : IGoogleOAuthClient
{
    public GoogleUserInfo? NextUserInfo { get; set; }
    public Exception? NextException { get; set; }

    public Task<GoogleTokenResponse> ExchangeCodeAsync(string code)
    {
        if (NextException is { } ex)
        {
            NextException = null;
            throw ex;
        }
        return Task.FromResult(new GoogleTokenResponse("fake-access-token", "fake-id-token", "Bearer", 3600));
    }

    public Task<GoogleUserInfo> GetUserInfoAsync(string accessToken)
    {
        var userInfo = NextUserInfo ?? new GoogleUserInfo("fake-sub", $"{Guid.NewGuid():N}@example.com", true, "Fake User");
        return Task.FromResult(userInfo);
    }
}
