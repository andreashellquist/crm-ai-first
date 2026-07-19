namespace CrmApi.Models;

public class Workspace
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string Name { get; set; }
    public string DefaultCurrency { get; set; } = "USD";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<WorkspaceMember> Members { get; set; } = [];
    public List<Contact> Contacts { get; set; } = [];
    public List<Company> Companies { get; set; } = [];
    public List<Deal> Deals { get; set; } = [];
    public List<Pipeline> Pipelines { get; set; } = [];
    public List<Activity> Activities { get; set; } = [];
    public List<TaskItem> Tasks { get; set; } = [];
    public WorkspaceSettings? Settings { get; set; }
    public List<FieldDefinition> FieldDefinitions { get; set; } = [];
    public List<Notification> Notifications { get; set; } = [];
    public List<SavedView> SavedViews { get; set; } = [];
}

public class WorkspaceMember
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string WorkspaceId { get; set; }
    public required string UserId { get; set; }
    public string Role { get; set; } = "member"; // owner | admin | member
    public string Timezone { get; set; } = "UTC";
    // Per-workspace access, not a whole-account flag — a SCIM-deprovisioned
    // user loses access to *this* workspace, not necessarily every
    // workspace they belong to. Set false by ScimUsersController; checked by
    // AuthController.Login. Note this doesn't revoke an already-issued JWT
    // (see auth-security-expert: this app has no server-side session store
    // to revoke against) — it blocks new logins, not existing sessions
    // until they expire.
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Workspace? Workspace { get; set; }
    public User? User { get; set; }
}
