namespace CrmApi.Models;

public class Contact
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string WorkspaceId { get; set; }
    public string? CompanyId { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string LifecycleStage { get; set; } = "lead";
    public string CustomFields { get; set; } = "{}"; // jsonb text, keyed by FieldDefinition.Key — see workspace-customization skill
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeletedAt { get; set; }

    public Workspace? Workspace { get; set; }
    public Company? Company { get; set; }
    public List<Deal> Deals { get; set; } = [];
    public List<Activity> Activities { get; set; } = [];
}
