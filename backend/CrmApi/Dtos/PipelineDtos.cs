using System.Text.Json;

namespace CrmApi.Dtos;

public record PipelineBoardDto(string Id, string Name, List<StageDto> Stages, string DefaultCurrency);

public record StageDto(string Id, string Name, List<DealCardDto> Deals);

public record DealCardDto(
    string Id,
    string Title,
    int? AmountCents,
    string? Currency,
    int? AiScore,
    string? AiScoreRationale
);

public record MoveDealRequest(string StageId);

// StageId is optional — omitted, it lands in the default pipeline's
// lowest-Order stage (its entry point), the same place a brand-new lead
// naturally starts. See PipelineController.CreateDeal.
public record CreateDealRequest(
    string CompanyName,
    string? StageId,
    int? AmountCents,
    string? Currency,
    string ForecastCategory,
    List<string>? ContactIds,
    Dictionary<string, JsonElement>? CustomFields
);

public record ScoreDealResponse(string JobId);

public record DealDetailDto(
    string Id,
    string Title,
    string StageName,
    int? AmountCents,
    string? Currency,
    string ForecastCategory,
    int? AiScore,
    string? AiScoreRationale,
    string? AiSummary,
    DateTime? AiSummarizedAt,
    int ActivitiesSinceSummary,
    List<string> ContactNames,
    List<ActivityDto> Activities,
    Dictionary<string, JsonElement> CustomFields
);

public record UpdateDealRequest(
    int? AmountCents,
    string? Currency,
    string ForecastCategory,
    Dictionary<string, JsonElement>? CustomFields
);

public record ActivityDto(string Id, string Type, string? Body, DateTime CreatedAt);

public record LogActivityRequest(string Type, string Body);

public record JobStatusResponse(string Status, string? LastError, string? Result);

public record DraftEmailRequest(string? Instruction);

public record EmailDraftResponse(string JobId);

public record SummarizeDealResponse(string JobId);

public record NextBestActionResponse(string JobId);

public record NextBestActionSuggestionDto(string Action, string Reasoning, string Confidence);
