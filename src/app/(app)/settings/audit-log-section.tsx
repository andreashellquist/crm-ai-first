import type { components } from "@/lib/api/schema";

type AuditLogEntry = components["schemas"]["AuditLogDto"];

const ACTION_LABELS: Record<string, string> = {
  "member.role_changed": "Changed a member's role",
  "member.removed": "Removed a member",
  "role.created": "Created a role",
  "role.updated": "Updated a role",
  "role.deleted": "Deleted a role",
  "api_key.created": "Created an API key",
  "api_key.revoked": "Revoked an API key",
  "sso_connection.updated": "Updated the SSO connection",
  "sso_connection.removed": "Removed the SSO connection",
  "workspace_settings.updated": "Updated workspace settings",
};

// Read-only — see AuditLogController/AuditLogService. Fetched with
// undefined vs. [] deliberately distinguished: undefined means the GET
// itself failed (not an owner/admin, since AuditLogController is gated
// class-wide), [] means it succeeded and nothing has been logged yet.
export function AuditLogSection({ entries }: { entries: AuditLogEntry[] | undefined }) {
  if (entries === undefined) {
    return <p className="text-sm text-neutral-500">Only an owner or admin can view the audit log.</p>;
  }

  if (entries.length === 0) {
    return <p className="text-sm text-neutral-500">No security-relevant changes logged yet.</p>;
  }

  return (
    <div className="overflow-hidden rounded-lg border border-neutral-200">
      <table className="w-full text-sm">
        <thead className="bg-neutral-50 text-left text-xs uppercase text-neutral-500">
          <tr>
            <th className="px-4 py-2 font-medium">When</th>
            <th className="px-4 py-2 font-medium">Who</th>
            <th className="px-4 py-2 font-medium">What</th>
          </tr>
        </thead>
        <tbody>
          {entries.map((entry) => (
            <tr key={entry.id} className="border-t border-neutral-100">
              <td className="whitespace-nowrap px-4 py-2 text-neutral-500">{new Date(entry.createdAt).toLocaleString()}</td>
              <td className="px-4 py-2">{entry.actorName ?? entry.actorEmail ?? "System"}</td>
              <td className="px-4 py-2">{ACTION_LABELS[entry.action] ?? entry.action}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
