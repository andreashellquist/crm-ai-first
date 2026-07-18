using System.Diagnostics;
using System.Text.Json;
using CrmApi.Data;
using CrmApi.Models;
using CrmApi.Observability;
using Microsoft.EntityFrameworkCore;

namespace CrmApi.Services;

record ScoreDealPayload(string DealId, string WorkspaceId);

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

    private static readonly Dictionary<string, Func<IServiceProvider, Job, Task>> Handlers = new()
    {
        ["score_deal"] = async (services, job) =>
        {
            var scoring = services.GetRequiredService<DealScoringService>();
            var payload = JsonSerializer.Deserialize<ScoreDealPayload>(job.Payload, PayloadOptions)
                ?? throw new InvalidOperationException("Invalid score_deal payload");
            await scoring.ScoreDeal(payload.DealId, payload.WorkspaceId);
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

                await handler(scope.ServiceProvider, job);
                await db.Jobs.Where(j => j.Id == job.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, "succeeded"), ct);
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
