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
record DeliverWebhookPayload(string DeliveryId);
record SweepOverdueTasksPayload();

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
        // Deliberately relies on this worker's own retry/backoff (Job.Attempts
        // vs MaxAttempts, exponential backoff below) rather than a bespoke
        // retry loop of its own — see the public-api-and-webhooks skill.
        // job.Attempts already reflects *this* attempt (incremented by the
        // claiming UPDATE in ProcessBatchAsync before the handler runs), so
        // comparing it to job.MaxAttempts here tells the handler whether this
        // is the terminal try, without needing to catch the retry decision
        // ProcessBatchAsync makes afterward.
        ["deliver_webhook"] = async (services, job) =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var payload = JsonSerializer.Deserialize<DeliverWebhookPayload>(job.Payload, PayloadOptions)
                ?? throw new InvalidOperationException("Invalid deliver_webhook payload");
            var delivery = await db.WebhookDeliveries.Include(d => d.Subscription)
                .FirstOrDefaultAsync(d => d.Id == payload.DeliveryId)
                ?? throw new InvalidOperationException("Webhook delivery not found");

            delivery.Attempts++;
            delivery.LastAttemptAt = DateTime.UtcNow;

            try
            {
                var httpClient = services.GetRequiredService<IHttpClientFactory>().CreateClient("webhooks");
                var request = new HttpRequestMessage(HttpMethod.Post, delivery.Subscription!.Url)
                {
                    Content = new StringContent(delivery.Payload, System.Text.Encoding.UTF8, "application/json"),
                };
                request.Headers.Add("X-Crm-Signature", WebhookSigner.Sign(delivery.Subscription.Secret, delivery.Payload));
                request.Headers.Add("X-Crm-Event", delivery.EventType);
                var response = await httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException($"Webhook endpoint returned HTTP {(int)response.StatusCode}");

                delivery.Status = "delivered";
                await db.SaveChangesAsync();
                return null;
            }
            catch
            {
                delivery.Status = job.Attempts >= job.MaxAttempts ? "failed" : "pending";
                await db.SaveChangesAsync();
                throw;
            }
        },
        // The task_overdue notification type has existed since the
        // notifications-and-digests skill's original design, but had no
        // real trigger — this app has no periodic-job scheduler (nothing
        // else in this codebase needs one; refresh_reports fires on writes,
        // not a timer). Rather than bolt on a separate scheduler
        // abstraction for one recurring job, this handler re-enqueues its
        // own next run as its last step, using the FOR UPDATE SKIP LOCKED
        // claim in ProcessBatchAsync to guarantee only one worker instance
        // ever runs a given sweep. Cross-workspace by design (WorkspaceId
        // null) — overdue tasks exist in every workspace, not one.
        ["sweep_overdue_tasks"] = async (services, job) =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var notifications = services.GetRequiredService<NotificationService>();
            var queue = services.GetRequiredService<JobQueueService>();

            var now = DateTime.UtcNow;
            var overdueTasks = await db.Tasks
                .Where(t => t.AssignedToUserId != null && t.CompletedAt == null && t.DueAt != null && t.DueAt < now)
                .ToListAsync();

            foreach (var task in overdueTasks)
            {
                // A sweep runs repeatedly forever, so it must not re-notify
                // the same overdue task every cycle — check for a prior
                // task_overdue notification for this task/assignee pair
                // rather than tracking a separate "already notified" flag.
                var alreadyNotified = await db.Notifications.AnyAsync(n =>
                    n.Type == "task_overdue" && n.EntityType == "task" && n.EntityId == task.Id && n.UserId == task.AssignedToUserId);
                if (alreadyNotified) continue;

                await notifications.Notify(task.WorkspaceId, task.AssignedToUserId!, "task_overdue", "task", task.Id);
            }

            await queue.Enqueue("sweep_overdue_tasks", new SweepOverdueTasksPayload(), workspaceId: null, runAt: now.Add(OverdueSweepInterval));
            return null;
        },
    };

    // 5 minutes is a modest, real cadence for this app's scale — not
    // configurable yet since nothing has needed that.
    private static readonly TimeSpan OverdueSweepInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Job worker started");
        await EnsureOverdueSweepScheduledAsync(stoppingToken);
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

    // Called once at startup, not on every restart cycle indefinitely — if
    // a sweep is already pending/processing (the common case: this app was
    // already running), a second one is never scheduled on top of it. Only
    // a genuinely fresh database (or one where every prior sweep somehow
    // reached "succeeded"/"failed" — the handler above always re-enqueues,
    // so that shouldn't normally happen) gets a new one seeded here.
    private async Task EnsureOverdueSweepScheduledAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var alreadyScheduled = await db.Jobs.AnyAsync(
            j => j.Type == "sweep_overdue_tasks" && (j.Status == "pending" || j.Status == "processing"), ct);
        if (alreadyScheduled) return;

        var queue = scope.ServiceProvider.GetRequiredService<JobQueueService>();
        await queue.Enqueue("sweep_overdue_tasks", new SweepOverdueTasksPayload(), workspaceId: null);
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
