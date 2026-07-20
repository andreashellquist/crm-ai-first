namespace CrmApi.Dtos;

public record PipelineReportRow(string StageId, string StageName, int DealCount, int DealValueCents, int WeightedValueCents);

public record ForecastReportRow(string ForecastCategory, int DealCount, int DealValueCents, int WeightedValueCents);

public record ActivityReportRow(DateOnly Date, string Type, int Count);

public record ReportsResponse(
    string DealTerm,
    string DealTermPlural,
    string DefaultCurrency,
    List<PipelineReportRow> Pipeline,
    List<ForecastReportRow> Forecast,
    List<ActivityReportRow> Activity,
    DateTime? RefreshedAt,
    string? RefreshedAtDisplay
);

public record RefreshReportsResponse(string JobId);
