using CrmApi.Services;

namespace CrmApi.Tests;

// Stands in for a real OIDC provider in every test — see qa-test-engineer:
// "never call a real third-party API in tests." Defaults to a canned
// verified user; tests override NextUserInfo/NextDiscoveryException/
// NextExchangeException before calling the exchange endpoint. Unlike
// FakeGoogleOAuthClient there's a real discovery step to fake too, since a
// workspace's connection names its own Issuer rather than a single fixed
// provider.
public class FakeOidcClient : IOidcClient
{
    public OidcUserInfo? NextUserInfo { get; set; }
    public Exception? NextDiscoveryException { get; set; }
    public Exception? NextExchangeException { get; set; }
    public string? LastExchangedCode { get; private set; }
    public string? LastExchangedRedirectUri { get; private set; }

    public Task<OidcDiscoveryDocument> DiscoverAsync(string issuer)
    {
        if (NextDiscoveryException is { } ex)
        {
            NextDiscoveryException = null;
            throw ex;
        }
        return Task.FromResult(new OidcDiscoveryDocument(
            $"{issuer}/authorize", $"{issuer}/token", $"{issuer}/userinfo"));
    }

    public Task<OidcTokenResponse> ExchangeCodeAsync(OidcDiscoveryDocument discovery, string clientId, string clientSecret, string code, string redirectUri)
    {
        LastExchangedCode = code;
        LastExchangedRedirectUri = redirectUri;
        if (NextExchangeException is { } ex)
        {
            NextExchangeException = null;
            throw ex;
        }
        return Task.FromResult(new OidcTokenResponse("fake-access-token", "fake-id-token", "Bearer", 3600));
    }

    public Task<OidcUserInfo> GetUserInfoAsync(OidcDiscoveryDocument discovery, string accessToken)
    {
        var userInfo = NextUserInfo ?? new OidcUserInfo("fake-sub", $"{Guid.NewGuid():N}@example.com", true, "Fake User");
        return Task.FromResult(userInfo);
    }
}
