namespace CrmApi.Models;

public class Company
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string WorkspaceId { get; set; }
    public required string Name { get; set; }
    public string? Domain { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeletedAt { get; set; }

    public Workspace? Workspace { get; set; }
    public List<Contact> Contacts { get; set; } = [];
    public List<Deal> Deals { get; set; } = [];
    public List<Activity> Activities { get; set; } = [];
}
