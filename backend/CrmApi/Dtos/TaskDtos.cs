namespace CrmApi.Dtos;

public record TaskDto(
    string Id,
    string Title,
    DateTime? DueAt,
    DateTime? CompletedAt,
    bool AiSuggested,
    string? ContactId,
    string? CompanyId,
    string? DealId,
    string? AssignedToUserId,
    string? AssignedToUserName
);

public record CreateTaskRequest(string Title, DateTime? DueAt, string? ContactId, string? CompanyId, string? DealId, string? AssignedToUserId = null);

public record UpdateTaskRequest(string Title, DateTime? DueAt, bool Completed, string? AssignedToUserId = null);
