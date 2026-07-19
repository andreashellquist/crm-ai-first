namespace CrmApi.Models;

// Read model — see the reporting-read-models skill and PipelineSnapshot's
// header comment (same fully-recomputed-per-refresh shape).
public class ForecastSnapshot
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string WorkspaceId { get; set; }
    public required string PipelineId { get; set; }
    public required string ForecastCategory { get; set; } // pipeline | best_case | commit | closed
    public int DealCount { get; set; }
    public int DealValueCents { get; set; }
    public int WeightedValueCents { get; set; }
    public DateTime RefreshedAt { get; set; } = DateTime.UtcNow;

    public Workspace? Workspace { get; set; }
}
