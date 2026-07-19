namespace CrmApi.Models;

// A workspace-scoped credential for the public v1 API — see the
// public-api-and-webhooks skill. The raw key is shown exactly once at
// creation (ApiKeysController.Create); only its SHA-256 hash is ever
// persisted (ApiKeyGenerator.Hash), so a leaked database dump can't be used
// to authenticate as a workspace.
public class ApiKey
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string WorkspaceId { get; set; }
    public required string Name { get; set; }
    public required string HashedKey { get; set; }
    public List<string> Scopes { get; set; } = [];
    public required string CreatedByUserId { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Workspace? Workspace { get; set; }
}
