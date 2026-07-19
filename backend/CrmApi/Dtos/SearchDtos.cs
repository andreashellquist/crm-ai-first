namespace CrmApi.Dtos;

public record SearchResultDto(string Id, string Label, string? Sublabel);

public record SearchResultsDto(
    List<SearchResultDto> Contacts,
    List<SearchResultDto> Companies,
    List<SearchResultDto> Deals
);
