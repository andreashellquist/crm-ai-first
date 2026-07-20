using System.Text.Json;
using CrmApi.Data;
using CrmApi.Models;

namespace CrmApi.Services;

// Writes AuditLog rows for a small, fixed catalog of security-relevant
// workspace-configuration changes — same "fixed catalog, not everything"
// discipline as WebhookDeliveryService's business-event catalog. Called
// synchronously from the controller action that makes the change (not
// queued — an audit write is cheap, and queuing it would let a job-queue
// outage silently drop audit trail entries, which defeats the point).
public class AuditLogService(AppDbContext db)
{
    public static class Actions
    {
        public const string MemberRoleChanged = "member.role_changed";
        public const string MemberRemoved = "member.removed";
        public const string RoleCreated = "role.created";
        public const string RoleUpdated = "role.updated";
        public const string RoleDeleted = "role.deleted";
        public const string ApiKeyCreated = "api_key.created";
        public const string ApiKeyRevoked = "api_key.revoked";
        public const string SsoConnectionUpdated = "sso_connection.updated";
        public const string SsoConnectionRemoved = "sso_connection.removed";
        public const string WorkspaceSettingsUpdated = "workspace_settings.updated";
    }

    // metadata is a plain object (anonymous type at call sites) serialized
    // to JSON — see AuditLog.Metadata's "ids/enums/booleans only, never PII"
    // rule. Does not call SaveChangesAsync itself: callers already save in
    // the same request, so this rides along in that same transaction rather
    // than issuing a second round-trip.
    public void Log(string workspaceId, string? actorUserId, string action, string? targetType = null, string? targetId = null, object? metadata = null)
    {
        db.AuditLogs.Add(new AuditLog
        {
            WorkspaceId = workspaceId,
            ActorUserId = actorUserId,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            Metadata = metadata is null ? "{}" : JsonSerializer.Serialize(metadata),
        });
    }
}
