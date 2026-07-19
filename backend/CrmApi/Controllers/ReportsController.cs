using CrmApi.Data;
using CrmApi.Dtos;
using CrmApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmApi.Controllers;

[ApiController]
[Route("api/reports")]
[Authorize]
public class ReportsController(AppDbContext db, CurrentUser current) : ControllerBase
{
    private static readonly string[] ValidExportTypes = ["pipeline", "forecast", "activity"];

    [HttpGet]
    public async Task<ActionResult<ReportsResponse>> Get()
    {
        var pipeline = await db.Pipelines
            .Where(p => p.WorkspaceId == current.WorkspaceId && p.IsDefault)
            .FirstOrDefaultAsync();
        if (pipeline is null) return NotFound();

        var (pipelineRows, forecastRows, activityRows, refreshedAt) = await LoadReportData(pipeline.Id);

        var terminologyJson = await db.WorkspaceSettings
            .Where(s => s.WorkspaceId == current.WorkspaceId)
            .Select(s => s.Terminology)
            .FirstOrDefaultAsync();
        var dealTerm = TerminologyResolver.Resolve(terminologyJson, "deal", "deal");
        var dealTermPlural = TerminologyResolver.Resolve(terminologyJson, "deal", "deals", plural: true);

        return Ok(new ReportsResponse(dealTerm, dealTermPlural, pipelineRows, forecastRows, activityRows, refreshedAt));
    }

    // Reports refresh on the writes that change what they show
    // (PipelineController.MoveDeal/UpdateDeal/LogActivity — see
    // ReportingService), which never fires for data that predates this
    // feature or was seeded directly. This lets a user force one instead of
    // waiting for the next tracked write.
    [HttpPost("refresh")]
    public async Task<ActionResult<RefreshReportsResponse>> Refresh([FromServices] JobQueueService queue)
    {
        var jobId = await queue.Enqueue("refresh_reports", new { workspaceId = current.WorkspaceId }, current.WorkspaceId);
        return Ok(new RefreshReportsResponse(jobId));
    }

    // Every report on screen is also a CSV download from the same read
    // model — see the reporting-read-models skill's "Export" section.
    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] string type)
    {
        if (!ValidExportTypes.Contains(type)) return BadRequest("Invalid export type");

        var pipeline = await db.Pipelines
            .Where(p => p.WorkspaceId == current.WorkspaceId && p.IsDefault)
            .FirstOrDefaultAsync();
        if (pipeline is null) return NotFound();

        var (pipelineRows, forecastRows, activityRows, _) = await LoadReportData(pipeline.Id);

        var csv = type switch
        {
            "pipeline" => CsvWriter.Write(
                ["Stage", "Deal count", "Deal value (cents)", "Weighted value (cents)"],
                pipelineRows.Select(r => new[] { r.StageName, r.DealCount.ToString(), r.DealValueCents.ToString(), r.WeightedValueCents.ToString() })),
            "forecast" => CsvWriter.Write(
                ["Forecast category", "Deal count", "Deal value (cents)", "Weighted value (cents)"],
                forecastRows.Select(r => new[] { r.ForecastCategory, r.DealCount.ToString(), r.DealValueCents.ToString(), r.WeightedValueCents.ToString() })),
            _ => CsvWriter.Write(
                ["Date", "Activity type", "Count"],
                activityRows.Select(r => new[] { r.Date.ToString("yyyy-MM-dd"), r.Type, r.Count.ToString() })),
        };

        return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", $"{type}-report.csv");
    }

    private async Task<(List<PipelineReportRow>, List<ForecastReportRow>, List<ActivityReportRow>, DateTime?)> LoadReportData(string pipelineId)
    {
        var stages = await db.Stages
            .Where(s => s.PipelineId == pipelineId)
            .OrderBy(s => s.Order)
            .Select(s => new { s.Id, s.Name })
            .ToListAsync();

        var pipelineSnapshots = await db.PipelineSnapshots
            .Where(s => s.WorkspaceId == current.WorkspaceId && s.PipelineId == pipelineId)
            .ToDictionaryAsync(s => s.StageId);
        var pipelineRows = stages
            .Select(stage => pipelineSnapshots.TryGetValue(stage.Id, out var snapshot)
                ? new PipelineReportRow(stage.Id, stage.Name, snapshot.DealCount, snapshot.DealValueCents, snapshot.WeightedValueCents)
                : new PipelineReportRow(stage.Id, stage.Name, 0, 0, 0))
            .ToList();

        var forecastRows = await db.ForecastSnapshots
            .Where(s => s.WorkspaceId == current.WorkspaceId && s.PipelineId == pipelineId)
            .Select(s => new ForecastReportRow(s.ForecastCategory, s.DealCount, s.DealValueCents, s.WeightedValueCents))
            .ToListAsync();

        var activityRows = await db.ActivityMetrics
            .Where(m => m.WorkspaceId == current.WorkspaceId)
            .OrderBy(m => m.Date)
            .Select(m => new ActivityReportRow(m.Date, m.Type, m.Count))
            .ToListAsync();

        var refreshedAt = pipelineSnapshots.Count > 0 ? pipelineSnapshots.Values.Max(s => s.RefreshedAt) : (DateTime?)null;

        return (pipelineRows, forecastRows, activityRows, refreshedAt);
    }
}
