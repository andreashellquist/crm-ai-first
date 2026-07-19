using System.Security.Cryptography;
using System.Text;

namespace CrmApi.Services;

// API keys are high-entropy random secrets (unlike user passwords), so a
// fast cryptographic hash is the right tool here — no need for bcrypt's
// deliberate slowness, which exists to blunt brute-forcing a low-entropy
// human-chosen password.
public static class ApiKeyGenerator
{
    private const string Prefix = "crm_live_";

    public static (string RawKey, string HashedKey) Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(bytes)
            .Replace("+", "")
            .Replace("/", "")
            .Replace("=", "");
        var rawKey = Prefix + token;
        return (rawKey, Hash(rawKey));
    }

    public static string Hash(string rawKey) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawKey)));
}
