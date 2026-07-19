using System.Diagnostics;
using System.Text.Json;
using CrmApi.Data;
using CrmApi.Models;
using CrmApi.Observability;
using Microsoft.EntityFrameworkCore;

namespace CrmApi.Services;

record ScoreDealPayload(string DealId, string WorkspaceId);
record DraftEmailPayload(string DealId, string WorkspaceId, string? Instruction);
record SummarizeDealPayload(string DealId, string WorkspaceId);
record NextBestActionPayload(string DealId, string WorkspaceId);
record ImportContactsPayload(string CsvContent, Dictionary<string, string> ColumnMapping, string WorkspaceId);
record RefreshReportsPayload(string WorkspaceId);

// Runs in-process as a hosted service for local dev / a dedicated deployment.
// ProcessBatchAsync is deliberately the unit both this loop and a future
// serverless/cron-triggered entrypoint would call — see the note in
// ExecuteAsync about where a persistent loop like this doesn't fit.
public class JobWorker(IServiceScopeFactory scopeFactory, ILogger<JobWorker> logger) : BackgroundService
{
    // Job payloads are written with System.Text.Json's default camelCase
    // property names (JobQueueService.Enqueue serializes an anonymous
    // object); this must match on deserialize or every field silently comes
    // back null/default instead of throwing — case-insensitive matching
    // avoids that footgun.
    private static readonly JsonSerializerOptions PayloadOptions = new() { PropertyNameCaseInsensitive = true };

    // Job.Result is read directly by the frontend as parsed JSON (see
    // getDraftJobStatusAction/getNextBestActionJobStatusAction), which
    // expects the same camelCase property names every controller-returned
    // DTO uses — JsonSerializer.Serialize's own default is PascalCase
    // (matching the C# record property names verbatim), which would silently
    // mismatch every field on the frontend.
    private static readonly JsonSerializerOptions ResultOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    // Handlers return an optional result payload (jsonb text) persisted to
    // Job.Result — for job types with nowhere else to write their output
    // (e.g. draft_email; score_deal returns null since it writes onto Deal
    // directly, same as before this existed).
    private static readonly Dictionary<string, Func<IServiceProvider, Job, Task<string?>>> Handlers = new()
    {
        ["score_deal"] = async (services, job) =>
        {
            var scoring = services.GetRequiredService<DealScoringService>();
            var payload = JsonSerializer.Deserialize<ScoreDealPayload>(job.Payload, PayloadOptions)
                ?? throw new InvalidOperationException("Invalid score_deal payload");
            await scoring.ScoreDeal(payload.DealId, payload.WorkspaceId);
            return null;
        },
        ["draft_email"] = async (services, job) =>
        {
            var drafting = services.GetRequiredService<EmailDraftingService>();
            var payload = JsonSerializer.Deserialize<DraftEmailPayload>(job.Payload, PayloadOptions)
                ?? throw new InvalidOperationException("Invalid draft_email payload");
            var result = await drafting.DraftEmail(payload.DealId, payload.WorkspaceId, payload.Instruction);
            return JsonSerializer.Serialize(result, ResultOptions);
        },
        ["summarize_deal"] = async (services, job) =>
        {
            var summarization = services.GetRequiredService<SummarizationService>();
            var payload = JsonSerializer.Deserialize<SummarizeDealPayload>(job.Payload, PayloadOptions)
                ?? throw new InvalidOperationException("Invalid summarize_deal payload");
            await summarization.SummarizeDeal(payload.DealId, payload.WorkspaceId);
            return null; // persisted onto Deal.AiSummary, same as score_deal
        },
        ["next_best_action"] = async (services, job) =>
        {
            var nextBestAction = services.GetRequiredService<NextBestActionService>();
            var payload = JsonSerializer.Deserialize<NextBestActionPayload>(job.Payload, PayloadOptions)
                ?? throw new InvalidOperationException("Invalid next_best_action payload");
            var result = await nextBestAction.SuggestActions(payload.DealId, payload.WorkspaceId);

            // "AI-generated suggestions/drafts becoming ready is itself a
            // notification-worthy event" — notifications-and-digests skill.
            // Only the requester is notified (RequestedByUserId, set when
            // PipelineController.NextBestAction enqueues); older/other job
            // types without a requester simply don't notify anyone.
            if (job.RequestedByUserId is { } requestedBy)
            {
                var notifications = services.GetRequiredService<NotificationService>();
                await notifications.Notify(payload.WorkspaceId, requestedBy, "ai_suggestion_ready", "deal", payload.DealId);
            }

            return JsonSerializer.Serialize(result, ResultOptions);
        },
        ["import_contacts"] = async (services, job) =>
        {
            var import = services.GetRequiredService<ContactImportService>();
            var payload = JsonSerializer.Deserialize<ImportContactsPayload>(job.Payload, PayloadOptions)
                ?? throw new InvalidOperationException("Invalid import_contacts payload");
            var result = await import.Import(payload.CsvContent, payload.ColumnMapping, payload.WorkspaceId);
            return JsonSerializer.Serialize(result, ResultOptions);
        },
        ["refresh_reports"] = async (services, job) =>
        {
            var reporting = services.GetRequiredService<ReportingService>();
            var payload = JsonSerializer.Deserialize<RefreshReportsPayload>(job.Payload, PayloadOptions)
                ?? throw new InvalidOperationException("Invalid refresh_reports payload");
            await reporting.RefreshWorkspaceReports(payload.WorkspaceId);
            return null; // persisted onto the read-model tables directly
        },
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Job worker started");
        while (!stoppingToken.IsCancellationRequested)
        {
            int claimed;
            try
            {
                claimed = await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            if (claimed == 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
        logger.LogInformation("Job worker stopped");
    }

    public async Task<int> ProcessBatchAsync(CancellationToken ct, int batchSize = 5)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Atomically claims due jobs via FOR UPDATE SKIP LOCKED so multiple
        // worker processes never double-process the same row.
        var jobs = await db.Jobs.FromSqlInterpolated($@"
            UPDATE ""Jobs""
            SET ""Status"" = 'processing', ""LockedAt"" = now(), ""Attempts"" = ""Attempts"" + 1, ""UpdatedAt"" = now()
            WHERE ""Id"" IN (
                SELECT ""Id"" FROM ""Jobs""
                WHERE ""Status"" = 'pending' AND ""RunAt"" <= now()
                ORDER BY ""RunAt"" ASC
                LIMIT {batchSize}
                FOR UPDATE SKIP LOCKED
            )
            RETURNING *
        ").AsNoTracking().ToListAsync(ct);

        foreach (var job in jobs)
        {
            // A job has no inbound HTTP request to hang a trace off of, so it
            // starts its own root span here — see observability-and-slo:
            // "a trace/request ID propagated through ... background job".
            using var activity = CrmApiActivitySource.Instance.StartActivity($"job.{job.Type}");
            activity?.SetTag("job.id", job.Id);
            activity?.SetTag("job.workspace_id", job.WorkspaceId);
            using var _ = logger.BeginScope(new Dictionary<string, object?>
            {
                ["WorkspaceId"] = job.WorkspaceId,
                ["JobId"] = job.Id,
                ["TraceId"] = activity?.TraceId.ToString(),
            });

            var startedAt = DateTime.UtcNow;
            try
            {
                if (!Handlers.TryGetValue(job.Type, out var handler))
                    throw new InvalidOperationException($"No handler registered for type \"{job.Type}\"");

                var result = await handler(scope.ServiceProvider, job);
                await db.Jobs.Where(j => j.Id == job.Id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(j => j.Status, "succeeded")
                        .SetProperty(j => j.Result, result), ct);
                activity?.SetStatus(ActivityStatusCode.Ok);
                logger.LogInformation(
                    "job_processed jobId={JobId} type={Type} outcome=succeeded durationMs={DurationMs}",
                    job.Id, job.Type, (DateTime.UtcNow - startedAt).TotalMilliseconds);
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                var willRetry = job.Attempts < job.MaxAttempts;
                if (willRetry)
                {
                    var backoffMs = Math.Min(2000 * Math.Pow(2, job.Attempts), 60_000);
                    var runAt = DateTime.UtcNow.AddMilliseconds(backoffMs);
                    await db.Jobs.Where(j => j.Id == job.Id).ExecuteUpdateAsync(s => s
                        .SetProperty(j => j.Status, "pending")
                        .SetProperty(j => j.RunAt, runAt)
                        .SetProperty(j => j.LastError, ex.Message), ct);
                }
                else
                {
                    await db.Jobs.Where(j => j.Id == job.Id).ExecuteUpdateAsync(s => s
                        .SetProperty(j => j.Status, "failed")
                        .SetProperty(j => j.LastError, ex.Message), ct);
                }
                logger.LogError(ex,
                    "job_processed jobId={JobId} type={Type} outcome={Outcome} attempts={Attempts} durationMs={DurationMs}",
                    job.Id, job.Type, willRetry ? "retrying" : "failed", job.Attempts, (DateTime.UtcNow - startedAt).TotalMilliseconds);
            }
        }

        return jobs.Count;
    }
}
