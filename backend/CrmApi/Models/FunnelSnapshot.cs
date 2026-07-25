namespace CrmApi.Models;

// Read model — same fully-recomputed-per-refresh shape as
// PipelineSnapshot/ForecastSnapshot (see reporting-read-models skill),
// computed from DealStageChange rather than from Deal's current state.
// EntryCount is "how many times a deal has ever entered this stage" —
// deliberately not "how many deals are currently in this stage" (that's
// PipelineSnapshot.DealCount) since a deal can revisit a stage.
public class FunnelSnapshot
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string WorkspaceId { get; set; }
    public required string PipelineId { get; set; }
    public required string StageId { get; set; }
    public int EntryCount { get; set; }
    public DateTime RefreshedAt { get; set; } = DateTime.UtcNow;

    public Workspace? Workspace { get; set; }
}
