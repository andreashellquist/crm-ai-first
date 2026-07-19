namespace CrmApi.Dtos;

public record ApiKeyDto(
    string Id,
    string Name,
    List<string> Scopes,
    DateTime? LastUsedAt,
    DateTime? RevokedAt,
    DateTime CreatedAt
);

public record CreateApiKeyRequest(string Name, List<string> Scopes);

// RawKey is only ever present on this one response — ApiKey.HashedKey is
// all that's persisted, so this is the caller's one chance to see it.
public record CreateApiKeyResponse(ApiKeyDto Key, string RawKey);
