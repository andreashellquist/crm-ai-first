namespace CrmApi.Models;

// A named, persisted filter/sort configuration a user can recall later — see
// frontend-engineer's "sortable/filterable via URL search params" convention:
// QueryString is exactly that URL search-params string (e.g.
// "q=jane&lifecycleStage=lead&sort=-createdAt"), so recalling a saved view is
// just navigating to "{listPageUrl}?{QueryString}". Per-user, not shared
// workspace-wide, matching NotificationPreference's per-user scoping — a
// "shared with the team" saved view is a real follow-up, not this v1.
public class SavedView
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string WorkspaceId { get; set; }
    public required string UserId { get; set; }
    public required string EntityType { get; set; } // "contact" for v1
    public required string Name { get; set; }
    public required string QueryString { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Workspace? Workspace { get; set; }
    public User? User { get; set; }
}
