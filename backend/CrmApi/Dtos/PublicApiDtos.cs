using System.Text.Json;

namespace CrmApi.Dtos;

// Canonical resource shapes for the public v1 API (public-api-and-webhooks
// skill) — field names never reflect a workspace's terminology overrides
// (that's a display-layer concern for this app's own UI), so an integrator
// gets a stable contract regardless of how a workspace has relabeled "Deal"
// or "Company" in the product.

public record PublicContactDto(
    string Id,
    string? FirstName,
    string? LastName,
    string? Email,
    string? Phone,
    string? CompanyId,
    string LifecycleStage,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    Dictionary<string, JsonElement> CustomFields
);

public record PublicCompanyDto(
    string Id,
    string Name,
    string? Domain,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    Dictionary<string, JsonElement> CustomFields
);

public record PublicDealDto(
    string Id,
    string? CompanyId,
    string PipelineId,
    string StageId,
    int? AmountCents,
    string? Currency,
    string ForecastCategory,
    List<string> ContactIds,
    DateTime? ClosedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    Dictionary<string, JsonElement> CustomFields
);
