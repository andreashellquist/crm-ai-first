namespace CrmApi.Models;

// Named TaskItem, not Task — "Task" collides with System.Threading.Tasks.Task,
// which every async method in this codebase returns. The DB table and API
// route are still "tasks"; only the C# type name differs.
public class TaskItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string WorkspaceId { get; set; }
    public required string Title { get; set; }
    public DateTime? DueAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public bool AiSuggested { get; set; }
    public string? ContactId { get; set; }
    public string? CompanyId { get; set; }
    public string? DealId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Workspace? Workspace { get; set; }
    public Contact? Contact { get; set; }
    public Company? Company { get; set; }
    public Deal? Deal { get; set; }
}
