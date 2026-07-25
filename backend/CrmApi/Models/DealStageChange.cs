namespace CrmApi.Models;

// Append-only log, not a read model — never deleted/recomputed the way
// PipelineSnapshot/ForecastSnapshot/ActivityMetric are. This is the real
// prerequisite the funnel/conversion report was blocked on: Deal only ever
// stored its *current* StageId, with no history of how it got there. See
// PipelineController.CreateDeal (FromStageId null = initial placement) and
// MoveDeal (FromStageId = the stage it left).
public class DealStageChange
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string WorkspaceId { get; set; }
    public required string DealId { get; set; }
    public required string PipelineId { get; set; }
    public string? FromStageId { get; set; }
    public required string ToStageId { get; set; }
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;

    public Workspace? Workspace { get; set; }
}
