namespace CrmApi.Dtos;

public record PipelineReportRow(string StageId, string StageName, int DealCount, int DealValueCents, int WeightedValueCents);

public record ForecastReportRow(string ForecastCategory, int DealCount, int DealValueCents, int WeightedValueCents);

public record ActivityReportRow(DateOnly Date, string Type, int Count);

// "How many times a deal has ever entered this stage" (DealStageChange log),
// not "how many deals are in this stage right now" (that's
// PipelineReportRow.DealCount) — see FunnelSnapshot's header comment.
public record FunnelReportRow(string StageId, string StageName, int EntryCount);

public record ReportsResponse(
    string DealTerm,
    string DealTermPlural,
    string DefaultCurrency,
    List<PipelineReportRow> Pipeline,
    List<ForecastReportRow> Forecast,
    List<ActivityReportRow> Activity,
    List<FunnelReportRow> Funnel,
    DateTime? RefreshedAt,
    string? RefreshedAtDisplay
);

public record RefreshReportsResponse(string JobId);
