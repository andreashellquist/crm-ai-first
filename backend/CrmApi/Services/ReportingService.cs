using CrmApi.Data;
using CrmApi.Models;
using Microsoft.EntityFrameworkCore;

namespace CrmApi.Services;

// Recomputes the reporting read models for one workspace — see the
// reporting-read-models skill. Fully recomputed (delete + reinsert) rather
// than incrementally updated, which makes this trivially idempotent and
// keeps the read models correct even if a refresh is ever run twice or out
// of order. Triggered by the writes that change what a report shows
// (PipelineController.MoveDeal/UpdateDeal/LogActivity), not on a timer —
// this app has no periodic-job scheduler, and refresh-on-write keeps
// reports current within one job-queue round trip of the change that
// caused them to go stale.
public class ReportingService(AppDbContext db)
{
    // Activity report is scoped to a rolling window — no report needs
    // activity history older than this, and it keeps ActivityMetrics
    // bounded regardless of a workspace's total historical Activity volume.
    private const int ActivityWindowDays = 30;

    public async Task RefreshWorkspaceReports(string workspaceId)
    {
        var deals = await db.Deals
            .Include(d => d.Stage)
            .Where(d => d.WorkspaceId == workspaceId && d.DeletedAt == null)
            .ToListAsync();

        // Pipeline report: OPEN deals only, by stage — "how much is in each
        // stage of the pipeline right now."
        var pipelineRows = deals
            .Where(d => d.ClosedAt == null)
            .GroupBy(d => (d.PipelineId, d.StageId))
            .Select(g => new PipelineSnapshot
            {
                WorkspaceId = workspaceId,
                PipelineId = g.Key.PipelineId,
                StageId = g.Key.StageId,
                DealCount = g.Count(),
                DealValueCents = g.Sum(d => d.AmountCents ?? 0),
                WeightedValueCents = g.Sum(d => WeightedCents(d)),
            })
            .ToList();

        // Forecast report: every deal (open or closed), by forecast
        // category — "closed" is itself one of the valid categories, so
        // this isn't filtered to open deals the way the pipeline report is.
        var forecastRows = deals
            .GroupBy(d => (d.PipelineId, d.ForecastCategory))
            .Select(g => new ForecastSnapshot
            {
                WorkspaceId = workspaceId,
                PipelineId = g.Key.PipelineId,
                ForecastCategory = g.Key.ForecastCategory,
                DealCount = g.Count(),
                DealValueCents = g.Sum(d => d.AmountCents ?? 0),
                WeightedValueCents = g.Sum(d => WeightedCents(d)),
            })
            .ToList();

        // Funnel report: how many times a deal has ever entered each stage
        // — computed from the append-only DealStageChange log, not from
        // Deal's current StageId, so a deal that moved through and back out
        // of a stage still counts as having entered it.
        var stageChanges = await db.DealStageChanges
            .Where(s => s.WorkspaceId == workspaceId)
            .ToListAsync();
        var funnelRows = stageChanges
            .GroupBy(s => (s.PipelineId, s.ToStageId))
            .Select(g => new FunnelSnapshot
            {
                WorkspaceId = workspaceId,
                PipelineId = g.Key.PipelineId,
                StageId = g.Key.ToStageId,
                EntryCount = g.Count(),
            })
            .ToList();

        var windowStart = DateTime.UtcNow.AddDays(-ActivityWindowDays);
        var activities = await db.Activities
            .Where(a => a.WorkspaceId == workspaceId && a.CreatedAt >= windowStart)
            .ToListAsync();
        var activityRows = activities
            .GroupBy(a => (Date: DateOnly.FromDateTime(a.CreatedAt), a.Type))
            .Select(g => new ActivityMetric
            {
                WorkspaceId = workspaceId,
                Date = g.Key.Date,
                Type = g.Key.Type,
                Count = g.Count(),
            })
            .ToList();

        await using var tx = await db.Database.BeginTransactionAsync();
        await db.PipelineSnapshots.Where(s => s.WorkspaceId == workspaceId).ExecuteDeleteAsync();
        await db.ForecastSnapshots.Where(s => s.WorkspaceId == workspaceId).ExecuteDeleteAsync();
        await db.ActivityMetrics.Where(m => m.WorkspaceId == workspaceId).ExecuteDeleteAsync();
        await db.FunnelSnapshots.Where(s => s.WorkspaceId == workspaceId).ExecuteDeleteAsync();

        db.PipelineSnapshots.AddRange(pipelineRows);
        db.ForecastSnapshots.AddRange(forecastRows);
        db.ActivityMetrics.AddRange(activityRows);
        db.FunnelSnapshots.AddRange(funnelRows);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    private static int WeightedCents(Deal d) => (d.AmountCents ?? 0) * (d.Stage?.Probability ?? 0) / 100;
}
