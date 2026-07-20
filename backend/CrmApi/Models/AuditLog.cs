namespace CrmApi.Models;

// A small, fixed catalog of security-relevant writes — same "fixed event
// catalog, not log-everything" discipline as WebhookSubscription.EventTypes
// — see AuditLogService for the actual Action strings and which controllers
// fire them. Read-only once written: no update path, no delete path (an
// audit trail that can be edited isn't one). This is the concrete piece
// closing the gap between docs/PRODUCT_SCOPE.md's claim that "audit logging
// is in the schema from Phase 0" and reality — no such table existed until
// this SOC 2 readiness pass.
public class AuditLog
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string WorkspaceId { get; set; }

    // Null for a system-initiated write with no human actor (none exist
    // yet — every wired action today is a direct user request — but the
    // column is nullable so a future scheduled/job-driven write doesn't
    // need a schema change to be logged truthfully instead of attributed
    // to whichever user happened to trigger the job).
    public string? ActorUserId { get; set; }

    // e.g. "member.role_changed", "api_key.created" — see AuditLogService.
    public required string Action { get; set; }
    public string? TargetType { get; set; } // e.g. "WorkspaceMember", "ApiKey"
    public string? TargetId { get; set; }

    // Small structured context (e.g. { "previousRole": "member", "newRole":
    // "admin" }) — deliberately not a full before/after row diff, which
    // would risk logging PII (a Contact's email, a Deal's amount) into a
    // trail with no redaction pass. Keep entries to ids/enums/booleans.
    public string Metadata { get; set; } = "{}";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Workspace? Workspace { get; set; }
    public User? ActorUser { get; set; }
}
