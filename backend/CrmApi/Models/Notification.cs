namespace CrmApi.Models;

// See the notifications-and-digests skill. entityType/entityId are a
// nullable polymorphic pointer (same convention as Activity/TaskItem) —
// e.g. "deal"/{dealId} for an ai_suggestion_ready notification.
public class Notification
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string WorkspaceId { get; set; }
    public required string UserId { get; set; }
    public required string Type { get; set; } // "ai_suggestion_ready" | "task_overdue"
    public string? EntityType { get; set; } // "deal" | "task"
    public string? EntityId { get; set; }
    public DateTime? ReadAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Workspace? Workspace { get; set; }
    public User? User { get; set; }
}
