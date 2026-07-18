namespace CrmApi.Models;

public class Activity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string WorkspaceId { get; set; }
    public required string Type { get; set; } // call | email | meeting | note
    public string? Body { get; set; }
    public string? ContactId { get; set; }
    public string? CompanyId { get; set; }
    public string? DealId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Workspace? Workspace { get; set; }
    public Contact? Contact { get; set; }
    public Company? Company { get; set; }
    public Deal? Deal { get; set; }
}
