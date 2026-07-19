namespace CrmApi.Models;

// Read model, rolling 30-day window — see the reporting-read-models skill.
// ReportingService recomputes only rows within the window on each refresh,
// which keeps this table bounded regardless of a workspace's total
// historical Activity volume (a deliberate v1 scope: no report currently
// needs activity history older than 30 days).
public class ActivityMetric
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string WorkspaceId { get; set; }
    public required DateOnly Date { get; set; }
    public required string Type { get; set; } // call | email | meeting | note
    public int Count { get; set; }
    public DateTime RefreshedAt { get; set; } = DateTime.UtcNow;

    public Workspace? Workspace { get; set; }
}
