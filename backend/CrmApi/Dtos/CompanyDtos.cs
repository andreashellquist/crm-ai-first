using System.Text.Json;

namespace CrmApi.Dtos;

public record CompanyDto(string Id, string Name, string? Domain, Dictionary<string, JsonElement> CustomFields);

public record UpdateCompanyRequest(string Name, string? Domain, Dictionary<string, JsonElement>? CustomFields);

// A real company detail page (previously didn't exist — global search
// results for companies had nowhere to link to but the contacts list) —
// distinct from the flat CompanyDto used by List/Update, since a detail
// view needs its associated contacts and deals too.
public record CompanyDetailDto(
    string Id,
    string Name,
    string? Domain,
    Dictionary<string, JsonElement> CustomFields,
    List<ContactOptionDto> Contacts,
    List<CompanyDetailDealDto> Deals
);

public record CompanyDetailDealDto(string Id, string StageName, int? AmountCents, string? Currency);
