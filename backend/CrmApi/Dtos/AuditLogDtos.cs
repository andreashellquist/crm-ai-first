namespace CrmApi.Dtos;

public record AuditLogDto(
    string Id,
    string? ActorUserId,
    string? ActorName,
    string? ActorEmail,
    string Action,
    string? TargetType,
    string? TargetId,
    string Metadata,
    DateTime CreatedAt
);
