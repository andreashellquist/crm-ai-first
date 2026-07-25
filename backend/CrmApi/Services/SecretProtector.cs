using Microsoft.AspNetCore.DataProtection;

namespace CrmApi.Services;

// Encrypts third-party credentials at rest (SsoConnection.ClientSecret,
// WebhookSubscription.Secret) — see auth-security-expert's "encrypt
// third-party credentials at rest" follow-up, tracked since the SSO and
// public-API/webhooks passes shipped these as plaintext. Uses ASP.NET
// Core's built-in Data Protection API rather than a bespoke crypto scheme.
//
// Scope limit, stated up front: this uses Data Protection's default local
// key ring (a file under the app's data-protection-keys directory), which
// is fine for this app's current single-instance deployment posture but
// would need a durable, shared key store (Azure Key Vault, Redis, a blob
// store) for a real multi-instance production deployment — not done here,
// same "don't build speculative infra" discipline as everywhere else in
// this app.
public interface ISecretProtector
{
    string Protect(string plaintext);
    string Unprotect(string protectedValue);
}

public class SecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    // A stable purpose string scopes the derived key to this specific use —
    // Data Protection guidance is to use one purpose per distinct kind of
    // data being protected, so a key compromise/rotation for one doesn't
    // implicate unrelated protected values elsewhere in the app.
    private readonly IDataProtector protector = provider.CreateProtector("CrmApi.SecretProtector.ThirdPartyCredentials.v1");

    public string Protect(string plaintext) => protector.Protect(plaintext);
    public string Unprotect(string protectedValue) => protector.Unprotect(protectedValue);
}
