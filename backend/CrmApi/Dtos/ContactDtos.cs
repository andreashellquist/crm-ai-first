using System.Text.Json;

namespace CrmApi.Dtos;

public record ContactDto(
    string Id,
    string? FirstName,
    string? LastName,
    string? Email,
    string? CompanyName,
    string LifecycleStage,
    Dictionary<string, JsonElement> CustomFields
);

public record CreateContactRequest(
    string FirstName,
    string? LastName,
    string? Email,
    string? CompanyName,
    Dictionary<string, JsonElement>? CustomFields = null
);
