using System.Text.Json;

namespace CrmApi.Dtos;

public record CompanyDto(string Id, string Name, string? Domain, Dictionary<string, JsonElement> CustomFields);

public record UpdateCompanyRequest(string Name, string? Domain, Dictionary<string, JsonElement>? CustomFields);
