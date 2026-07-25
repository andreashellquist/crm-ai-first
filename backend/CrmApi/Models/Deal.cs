namespace CrmApi.Models;

public class Deal
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string WorkspaceId { get; set; }
    public required string PipelineId { get; set; }
    public required string StageId { get; set; }
    public string? CompanyId { get; set; }
    // Who owns working this deal — previously nothing modeled this at all,
    // which is why the deal_assigned notification trigger had no real event
    // to fire on. See PipelineController.AssignDeal.
    public string? AssignedToUserId { get; set; }
    public int? AmountCents { get; set; }
    public string? Currency { get; set; }
    public string ForecastCategory { get; set; } = "pipeline";
    public string CustomFields { get; set; } = "{}"; // jsonb text, keyed by FieldDefinition.Key — see workspace-customization skill
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

    // AI deal/activity summary — cached (summarize-on-read, not on every
    // page load) and updated incrementally: SummarizationService only sends
    // Claude the activities logged after AiSummarizedAt plus the prior
    // summary text, folding them in, rather than re-summarizing the whole
    // history each time. See SummarizationService and the ai-features-architect
    // agent's "Summarization" pattern.
    public string? AiSummary { get; set; }
    public DateTime? AiSummarizedAt { get; set; }

    public Workspace? Workspace { get; set; }
    public Pipeline? Pipeline { get; set; }
    public Stage? Stage { get; set; }
    public Company? Company { get; set; }
    public User? AssignedToUser { get; set; }
    public List<Contact> Contacts { get; set; } = [];
    public List<Activity> Activities { get; set; } = [];
    public List<TaskItem> Tasks { get; set; } = [];
}
