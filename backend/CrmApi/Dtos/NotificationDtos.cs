namespace CrmApi.Dtos;

public record NotificationDto(
    string Id,
    string Type,
    string? EntityType,
    string? EntityId,
    DateTime? ReadAt,
    DateTime CreatedAt
);

public record UnreadCountResponse(int Count);
