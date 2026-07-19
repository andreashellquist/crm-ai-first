namespace CrmApi.Models;

// Read model, not a live aggregate — see the reporting-read-models skill.
// Fully recomputed (delete + reinsert) per workspace on each refresh, not
// incrementally updated, which keeps ReportingService trivially idempotent.
public class PipelineSnapshot
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string WorkspaceId { get; set; }
    public required string PipelineId { get; set; }
    public required string StageId { get; set; }
    public int DealCount { get; set; }
    public int DealValueCents { get; set; }
    public int WeightedValueCents { get; set; } // dealValueCents weighted by Stage.Probability
    public DateTime RefreshedAt { get; set; } = DateTime.UtcNow;

    public Workspace? Workspace { get; set; }
}
