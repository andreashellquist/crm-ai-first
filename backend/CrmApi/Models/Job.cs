namespace CrmApi.Models;

// Background jobs — anything that calls an LLM or a third-party API runs
// here, off the request path, not inline (backend-api-engineer).
public class Job
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string? WorkspaceId { get; set; }
    // Who asked for this job, when that's meaningful (e.g. so a completing
    // next_best_action job knows who to notify) — null for job types with no
    // single requesting user (none currently) or where the queueing endpoint
    // predates this column.
    public string? RequestedByUserId { get; set; }
    public required string Type { get; set; } // e.g. "score_deal"
    public required string Payload { get; set; } // jsonb text
    public string Status { get; set; } = "pending"; // pending | processing | succeeded | failed
    public int Attempts { get; set; }
    public int MaxAttempts { get; set; } = 3;
    public DateTime RunAt { get; set; } = DateTime.UtcNow;
    public DateTime? LockedAt { get; set; }
    public string? LastError { get; set; }
    // jsonb text — for job types whose output has nowhere else to live (e.g.
    // an email draft isn't a field on any entity, unlike deal scoring which
    // writes onto Deal). Null for job types that persist their own result.
    public string? Result { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
