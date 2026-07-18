namespace CrmApi.Models;

// Background jobs — anything that calls an LLM or a third-party API runs
// here, off the request path, not inline (backend-api-engineer).
public class Job
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string? WorkspaceId { get; set; }
    public required string Type { get; set; } // e.g. "score_deal"
    public required string Payload { get; set; } // jsonb text
    public string Status { get; set; } = "pending"; // pending | processing | succeeded | failed
    public int Attempts { get; set; }
    public int MaxAttempts { get; set; } = 3;
    public DateTime RunAt { get; set; } = DateTime.UtcNow;
    public DateTime? LockedAt { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
