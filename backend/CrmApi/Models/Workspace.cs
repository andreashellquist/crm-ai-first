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
}

public class WorkspaceMember
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string WorkspaceId { get; set; }
    public required string UserId { get; set; }
    public string Role { get; set; } = "member"; // owner | admin | member
    public string Timezone { get; set; } = "UTC";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Workspace? Workspace { get; set; }
    public User? User { get; set; }
}
