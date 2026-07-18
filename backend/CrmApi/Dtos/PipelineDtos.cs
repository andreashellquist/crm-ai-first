namespace CrmApi.Dtos;

public record PipelineBoardDto(string Id, string Name, List<StageDto> Stages);

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

public record ScoreDealResponse(string JobId);

public record DealDetailDto(
    string Id,
    string Title,
    string StageName,
    int? AmountCents,
    string? Currency,
    int? AiScore,
    string? AiScoreRationale,
    List<string> ContactNames,
    List<ActivityDto> Activities
);

public record ActivityDto(string Id, string Type, string? Body, DateTime CreatedAt);

public record LogActivityRequest(string Type, string Body);

public record JobStatusResponse(string Status, string? LastError);
