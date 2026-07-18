namespace CrmApi.Dtos;

public record TaskDto(
    string Id,
    string Title,
    DateTime? DueAt,
    DateTime? CompletedAt,
    bool AiSuggested,
    string? ContactId,
    string? CompanyId,
    string? DealId
);

public record CreateTaskRequest(string Title, DateTime? DueAt, string? ContactId, string? CompanyId, string? DealId);

public record UpdateTaskRequest(string Title, DateTime? DueAt, bool Completed);
