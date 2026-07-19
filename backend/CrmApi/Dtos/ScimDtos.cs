using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrmApi.Dtos;

// SCIM 2.0 (RFC 7643/7644) resource shapes. Every property carries an
// explicit [JsonPropertyName] rather than relying on this app's default
// camelCase serialization policy — SCIM's wire format is case-sensitive and
// two attributes ("Resources", "Operations") are capitalized against the
// otherwise-camelCase grain, which the global policy would otherwise mangle.

public record ScimName(
    [property: JsonPropertyName("givenName")] string? GivenName,
    [property: JsonPropertyName("familyName")] string? FamilyName
);

public record ScimEmail(
    [property: JsonPropertyName("value")] string Value,
    [property: JsonPropertyName("primary")] bool Primary
);

public record ScimMeta(
    [property: JsonPropertyName("resourceType")] string ResourceType,
    [property: JsonPropertyName("created")] DateTime Created,
    [property: JsonPropertyName("lastModified")] DateTime LastModified
);

public record ScimUserDto(
    [property: JsonPropertyName("schemas")] List<string> Schemas,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("userName")] string UserName,
    [property: JsonPropertyName("name")] ScimName Name,
    [property: JsonPropertyName("emails")] List<ScimEmail> Emails,
    [property: JsonPropertyName("active")] bool Active,
    [property: JsonPropertyName("meta")] ScimMeta Meta
);

public record ScimListResponse(
    [property: JsonPropertyName("schemas")] List<string> Schemas,
    [property: JsonPropertyName("totalResults")] int TotalResults,
    [property: JsonPropertyName("startIndex")] int StartIndex,
    [property: JsonPropertyName("itemsPerPage")] int ItemsPerPage,
    [property: JsonPropertyName("Resources")] List<ScimUserDto> Resources
);

public record ScimError(
    [property: JsonPropertyName("schemas")] List<string> Schemas,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("detail")] string Detail,
    [property: JsonPropertyName("scimType")] string? ScimType = null
);

public record ScimCreateUserRequest(
    [property: JsonPropertyName("userName")] string UserName,
    [property: JsonPropertyName("name")] ScimName? Name,
    [property: JsonPropertyName("active")] bool? Active
);

public record ScimReplaceUserRequest(
    [property: JsonPropertyName("name")] ScimName? Name,
    [property: JsonPropertyName("active")] bool? Active
);

public record ScimPatchOperation(
    [property: JsonPropertyName("op")] string Op,
    [property: JsonPropertyName("path")] string? Path,
    [property: JsonPropertyName("value")] JsonElement Value
);

public record ScimPatchRequest(
    [property: JsonPropertyName("schemas")] List<string>? Schemas,
    [property: JsonPropertyName("Operations")] List<ScimPatchOperation> Operations
);
