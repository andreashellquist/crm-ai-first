namespace CrmApi.Models;

// Per-user, not per-workspace — matches the notifications-and-digests skill's
// shape. This app currently has no workspace-switcher (a session resolves to
// a single membership at login — see lib/workspace.ts), so per-workspace
// preferences aren't yet a real gap; revisit if/when multi-workspace
// switching ships.
public class NotificationPreference
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string UserId { get; set; }
    public required string Type { get; set; } // matches Notification.Type
    public bool InApp { get; set; } = true;
    // "off" | "immediate" | "daily" | "weekly" — stored for forward
    // compatibility but not yet consumed: there's no actual email-sending
    // capability in this app yet at all (see CLAUDE.md's "notably not yet
    // built"), so no digest job reads this today.
    public string EmailDigest { get; set; } = "daily";

    public User? User { get; set; }
}
