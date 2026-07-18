using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace CrmApi.Services;

// Real implementation, wrapping Google's OAuth 2.0 token + userinfo
// endpoints directly over HttpClient — no dedicated OAuth NuGet package, so
// the exchange is fully visible here rather than hidden behind a library.
// Registered as a typed client (AddHttpClient<IGoogleOAuthClient, ...>) in
// Program.cs, not a bare `new HttpClient()` (avoids socket exhaustion).
//
// SCAFFOLD ONLY: GoogleOAuth:ClientId/ClientSecret/RedirectUri are unset by
// default. This code path compiles and is fully reviewable, but Google
// sign-in will not work until real credentials from Google Cloud Console
// are configured — see README.md.
public class GoogleOAuthClient(HttpClient http, IConfiguration config) : IGoogleOAuthClient
{
    public async Task<GoogleTokenResponse> ExchangeCodeAsync(string code)
    {
        var clientId = config["GoogleOAuth:ClientId"];
        var clientSecret = config["GoogleOAuth:ClientSecret"];
        var redirectUri = config["GoogleOAuth:RedirectUri"];
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret) || string.IsNullOrWhiteSpace(redirectUri))
            throw new InvalidOperationException("GoogleOAuth:ClientId/ClientSecret/RedirectUri are not configured");

        var response = await http.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code",
        }));
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new GoogleTokenResponse(
            payload.GetProperty("access_token").GetString()!,
            payload.GetProperty("id_token").GetString()!,
            payload.GetProperty("token_type").GetString()!,
            payload.GetProperty("expires_in").GetInt32());
    }

    public async Task<GoogleUserInfo> GetUserInfoAsync(string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/oauth2/v3/userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new GoogleUserInfo(
            payload.GetProperty("sub").GetString()!,
            payload.GetProperty("email").GetString()!,
            payload.TryGetProperty("email_verified", out var verified) && verified.ValueKind == JsonValueKind.True,
            payload.TryGetProperty("name", out var name) ? name.GetString() : null);
    }
}
