namespace CrmApi.Models;

public class Deal
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string WorkspaceId { get; set; }
    public required string PipelineId { get; set; }
    public required string StageId { get; set; }
    public string? CompanyId { get; set; }
    public int? AmountCents { get; set; }
    public string? Currency { get; set; }
    public string ForecastCategory { get; set; } = "pipeline";
    public DateTime? ClosedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeletedAt { get; set; }

    // AI deal scoring — cached so the UI has something to show instantly.
    // Stored as jsonb text; see DealScoringService for the shape.
    public int? AiScore { get; set; }
    public string? AiScoreRationale { get; set; }
    public string? AiScoreSignals { get; set; }
    public DateTime? AiScoredAt { get; set; }

    public Workspace? Workspace { get; set; }
    public Pipeline? Pipeline { get; set; }
    public Stage? Stage { get; set; }
    public Company? Company { get; set; }
    public List<Contact> Contacts { get; set; } = [];
    public List<Activity> Activities { get; set; } = [];
}
