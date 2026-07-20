using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace CrmApi.Services;

// Real implementation, wrapping a generic OIDC provider's discovery + token
// + userinfo endpoints directly over HttpClient — same "no dedicated OAuth
// package, exchange fully visible here" rationale as GoogleOAuthClient.
// Registered as a typed client (AddHttpClient<IOidcClient, ...>) in
// Program.cs. Deliberately does not validate the ID token's JWT signature
// against the provider's JWKS — like GoogleOAuthClient, it trusts the
// token/userinfo endpoints directly over a server-to-server TLS connection
// (the code exchange itself, not a client-side ID token check, is what
// proves the code came from a real sign-in) rather than adding a JWKS
// fetch/cache/verify pipeline for a feature no real IdP has exercised yet.
//
// SCAFFOLD ONLY: no default Issuer/ClientId/ClientSecret — those come from
// each workspace's SsoConnection row, configured by a workspace admin
// against their own real IdP. This code path compiles and is fully
// reviewable/testable (see FakeOidcClient), but SSO sign-in will not work
// until a workspace admin configures a real IdP connection.
public class OidcClient(HttpClient http) : IOidcClient
{
    public async Task<OidcDiscoveryDocument> DiscoverAsync(string issuer)
    {
        var response = await http.GetAsync($"{issuer.TrimEnd('/')}/.well-known/openid-configuration");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new OidcDiscoveryDocument(
            payload.GetProperty("authorization_endpoint").GetString()!,
            payload.GetProperty("token_endpoint").GetString()!,
            payload.GetProperty("userinfo_endpoint").GetString()!);
    }

    public async Task<OidcTokenResponse> ExchangeCodeAsync(OidcDiscoveryDocument discovery, string clientId, string clientSecret, string code, string redirectUri)
    {
        var response = await http.PostAsync(discovery.TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code",
        }));
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new OidcTokenResponse(
            payload.GetProperty("access_token").GetString()!,
            payload.TryGetProperty("id_token", out var idToken) ? idToken.GetString() : null,
            payload.GetProperty("token_type").GetString()!,
            payload.GetProperty("expires_in").GetInt32());
    }

    public async Task<OidcUserInfo> GetUserInfoAsync(OidcDiscoveryDocument discovery, string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, discovery.UserinfoEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new OidcUserInfo(
            payload.GetProperty("sub").GetString()!,
            payload.GetProperty("email").GetString()!,
            payload.TryGetProperty("email_verified", out var verified) && verified.ValueKind == JsonValueKind.True,
            payload.TryGetProperty("name", out var name) ? name.GetString() : null);
    }
}
