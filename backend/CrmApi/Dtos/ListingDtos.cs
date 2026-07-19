namespace CrmApi.Dtos;

public record ListingDto(
    string DealId,
    string? ListingAgentName,
    string? ListingUrl,
    DateTime? OpenHouseAt,
    decimal? CommissionPercent,
    DateTime UpdatedAt
);

public record UpsertListingRequest(
    string? ListingAgentName,
    string? ListingUrl,
    DateTime? OpenHouseAt,
    decimal? CommissionPercent
);
