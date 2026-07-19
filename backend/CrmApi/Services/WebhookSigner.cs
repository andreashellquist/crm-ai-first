using System.Security.Cryptography;
using System.Text;

namespace CrmApi.Services;

// HMAC-SHA256 signing for outbound webhook payloads — see the
// public-api-and-webhooks skill. A receiver recomputes this over the raw
// request body with its subscription's secret and compares against the
// X-Crm-Signature header to verify the payload actually came from this app.
public static class WebhookSigner
{
    public static string Sign(string secret, string payload)
    {
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload));
        return "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string GenerateSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).Replace("+", "").Replace("/", "").Replace("=", "");
    }
}
