using System.Net.Http.Json;
using System.Text.Json;
using CrmApi.Dtos;
using CrmApi.Services;
using Microsoft.EntityFrameworkCore;

namespace CrmApi.Tests;

// Covers the score_deal path end-to-end through the real queue: enqueue via
// the HTTP endpoint, then drive the worker directly via
// IntegrationTestBase.ProcessAllPendingJobsAsync (skipping its 2s idle-poll
// loop, which is disabled entirely in the test host — see CrmApiFactory).
[Collection(CrmApiCollection.Name)]
public class JobQueueIntegrationTests(CrmApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task ScoreDealJob_OnSuccess_UpdatesJobAndDealAiScore()
    {
        var ws = await SeedWorkspaceAsync();
        var deal = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne);
        await WithDb(async db => { db.Deals.Add(deal); await db.SaveChangesAsync(); });

        Anthropic.NextResponse = _ => FakeAnthropicMessagesClient.DefaultScoreMessage(score: 61, rationale: "Fake happy path.");
        try
        {
            var scoreResponse = await ws.Client.PostAsync($"/api/deals/{deal.Id}/score", content: null);
            scoreResponse.EnsureSuccessStatusCode();
            var enqueued = await scoreResponse.Content.ReadFromJsonAsync<ScoreDealResponse>();
            Assert.NotNull(enqueued);

            await ProcessAllPendingJobsAsync();

            var statusResponse = await ws.Client.GetFromJsonAsync<JobStatusResponse>($"/api/jobs/{enqueued!.JobId}");
            Assert.NotNull(statusResponse);
            Assert.Equal("succeeded", statusResponse!.Status);

            var persisted = await WithDb(db => db.Deals.SingleAsync(d => d.Id == deal.Id));
            Assert.Equal(61, persisted.AiScore);
            Assert.Equal("Fake happy path.", persisted.AiScoreRationale);
        }
        finally
        {
            Anthropic.NextResponse = null;
        }
    }

    [Fact]
    public async Task ScoreDealJob_OnFailure_RecordsLastErrorAndSchedulesRetry()
    {
        var ws = await SeedWorkspaceAsync();
        var deal = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne);
        await WithDb(async db => { db.Deals.Add(deal); await db.SaveChangesAsync(); });

        Anthropic.NextException = new HttpRequestException("simulated outage");
        try
        {
            var scoreResponse = await ws.Client.PostAsync($"/api/deals/{deal.Id}/score", content: null);
            scoreResponse.EnsureSuccessStatusCode();
            var enqueued = await scoreResponse.Content.ReadFromJsonAsync<ScoreDealResponse>();

            await ProcessAllPendingJobsAsync();

            var statusResponse = await ws.Client.GetFromJsonAsync<JobStatusResponse>($"/api/jobs/{enqueued!.JobId}");
            Assert.NotNull(statusResponse);
            // First attempt of MaxAttempts=3 fails — the job is put back to
            // pending with a future RunAt for retry, not terminal "failed" yet.
            Assert.Equal("pending", statusResponse!.Status);
            Assert.Equal("Deal scoring is temporarily unavailable", statusResponse.LastError);

            var persisted = await WithDb(db => db.Deals.SingleAsync(d => d.Id == deal.Id));
            Assert.Null(persisted.AiScore);
        }
        finally
        {
            Anthropic.NextException = null;
        }
    }

    [Fact]
    public async Task DraftEmailJob_OnSuccess_PersistsResultOnJob()
    {
        var ws = await SeedWorkspaceAsync();
        var deal = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne);
        await WithDb(async db => { db.Deals.Add(deal); await db.SaveChangesAsync(); });

        Anthropic.NextResponse = _ => FakeAnthropicMessagesClient.DefaultDraftMessage("Quick follow-up", "Hi there, following up on our chat.");
        try
        {
            var draftResponse = await ws.Client.PostAsJsonAsync($"/api/deals/{deal.Id}/draft-email", new DraftEmailRequest(null));
            draftResponse.EnsureSuccessStatusCode();
            var enqueued = await draftResponse.Content.ReadFromJsonAsync<EmailDraftResponse>();
            Assert.NotNull(enqueued);

            await ProcessAllPendingJobsAsync();

            var statusResponse = await ws.Client.GetFromJsonAsync<JobStatusResponse>($"/api/jobs/{enqueued!.JobId}");
            Assert.NotNull(statusResponse);
            Assert.Equal("succeeded", statusResponse!.Status);
            Assert.NotNull(statusResponse.Result);
            // The frontend parses Job.Result as plain JSON expecting camelCase
            // keys (getDraftJobStatusAction reads data.subject/data.body) — this
            // is a case-sensitive check, not just a deserialize-anything check.
            Assert.Contains("\"subject\":", statusResponse.Result);
            Assert.Contains("\"body\":", statusResponse.Result);

            var result = JsonSerializer.Deserialize<EmailDraftResult>(statusResponse.Result!, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert.Equal("Quick follow-up", result!.Subject);
            Assert.Equal("Hi there, following up on our chat.", result.Body);
        }
        finally
        {
            Anthropic.NextResponse = null;
        }
    }

    [Fact]
    public async Task SummarizeDealJob_OnSuccess_UpdatesDealAiSummary()
    {
        var ws = await SeedWorkspaceAsync();
        var deal = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne);
        await WithDb(async db => { db.Deals.Add(deal); await db.SaveChangesAsync(); });

        Anthropic.NextResponse = _ => FakeAnthropicMessagesClient.DefaultSummaryMessage("Fresh summary via the queue.");
        try
        {
            var summarizeResponse = await ws.Client.PostAsync($"/api/deals/{deal.Id}/summarize", content: null);
            summarizeResponse.EnsureSuccessStatusCode();
            var enqueued = await summarizeResponse.Content.ReadFromJsonAsync<SummarizeDealResponse>();
            Assert.NotNull(enqueued);

            await ProcessAllPendingJobsAsync();

            var statusResponse = await ws.Client.GetFromJsonAsync<JobStatusResponse>($"/api/jobs/{enqueued!.JobId}");
            Assert.NotNull(statusResponse);
            Assert.Equal("succeeded", statusResponse!.Status);

            var persisted = await WithDb(db => db.Deals.SingleAsync(d => d.Id == deal.Id));
            Assert.Equal("Fresh summary via the queue.", persisted.AiSummary);
        }
        finally
        {
            Anthropic.NextResponse = null;
        }
    }

    [Fact]
    public async Task NextBestActionJob_OnSuccess_PersistsResultOnJob()
    {
        var ws = await SeedWorkspaceAsync();
        var deal = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne);
        await WithDb(async db => { db.Deals.Add(deal); await db.SaveChangesAsync(); });

        Anthropic.NextResponse = _ => FakeAnthropicMessagesClient.DefaultSuggestActionsMessage(
            ("Send a follow-up email", "No recent contact in the last week.", "medium"));
        try
        {
            var nbaResponse = await ws.Client.PostAsync($"/api/deals/{deal.Id}/next-best-action", content: null);
            nbaResponse.EnsureSuccessStatusCode();
            var enqueued = await nbaResponse.Content.ReadFromJsonAsync<NextBestActionResponse>();
            Assert.NotNull(enqueued);

            await ProcessAllPendingJobsAsync();

            var statusResponse = await ws.Client.GetFromJsonAsync<JobStatusResponse>($"/api/jobs/{enqueued!.JobId}");
            Assert.NotNull(statusResponse);
            Assert.Equal("succeeded", statusResponse!.Status);
            Assert.NotNull(statusResponse.Result);
            // getNextBestActionJobStatusAction parses this as { suggestions: [...] } —
            // a case-sensitive check that the payload is actually camelCase.
            Assert.Contains("\"suggestions\":", statusResponse.Result);
            Assert.Contains("Send a follow-up email", statusResponse.Result);

            // "AI-generated suggestions/drafts becoming ready is itself a
            // notification-worthy event" — notifications-and-digests skill.
            var notification = await WithDb(db => db.Notifications.SingleAsync(n => n.WorkspaceId == ws.Workspace.Id));
            Assert.Equal(ws.User.Id, notification.UserId); // the requester, per Job.RequestedByUserId
            Assert.Equal("ai_suggestion_ready", notification.Type);
            Assert.Equal("deal", notification.EntityType);
            Assert.Equal(deal.Id, notification.EntityId);
        }
        finally
        {
            Anthropic.NextResponse = null;
        }
    }

    [Fact]
    public async Task ImportContactsJob_OnSuccess_PersistsResultOnJob()
    {
        var ws = await SeedWorkspaceAsync();
        var csv = "Email,First,Last\njane@acme.com,Jane,Doe";
        var mapping = new Dictionary<string, string> { ["email"] = "Email", ["firstName"] = "First", ["lastName"] = "Last" };

        var importResponse = await ws.Client.PostAsJsonAsync("/api/contacts/import", new CsvImportRequest(csv, mapping));
        importResponse.EnsureSuccessStatusCode();
        var enqueued = await importResponse.Content.ReadFromJsonAsync<CsvImportResponse>();
        Assert.NotNull(enqueued);

        await ProcessAllPendingJobsAsync();

        var statusResponse = await ws.Client.GetFromJsonAsync<JobStatusResponse>($"/api/jobs/{enqueued!.JobId}");
        Assert.NotNull(statusResponse);
        Assert.Equal("succeeded", statusResponse!.Status);
        Assert.NotNull(statusResponse.Result);
        // getImportJobStatusAction parses this as { created, updated, skipped,
        // errors } — a case-sensitive check that the payload is camelCase.
        Assert.Contains("\"created\":1", statusResponse.Result);

        var persisted = await WithDb(db => db.Contacts.SingleAsync(c => c.WorkspaceId == ws.Workspace.Id && c.Email == "jane@acme.com"));
        Assert.Equal("Jane", persisted.FirstName);
    }

    [Fact]
    public async Task RefreshReportsJob_OnSuccess_WritesReadModelRows()
    {
        var ws = await SeedWorkspaceAsync();
        var deal = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne, amountCents: 200_00);
        await WithDb(async db => { db.Deals.Add(deal); await db.SaveChangesAsync(); });

        var refreshResponse = await ws.Client.PostAsync("/api/reports/refresh", content: null);
        refreshResponse.EnsureSuccessStatusCode();
        var enqueued = await refreshResponse.Content.ReadFromJsonAsync<RefreshReportsResponse>();
        Assert.NotNull(enqueued);

        await ProcessAllPendingJobsAsync();

        var statusResponse = await ws.Client.GetFromJsonAsync<JobStatusResponse>($"/api/jobs/{enqueued!.JobId}");
        Assert.Equal("succeeded", statusResponse!.Status);

        var snapshot = await WithDb(db => db.PipelineSnapshots.SingleAsync(s => s.WorkspaceId == ws.Workspace.Id && s.StageId == ws.StageOne.Id));
        Assert.Equal(1, snapshot.DealCount);
        Assert.Equal(200_00, snapshot.DealValueCents);
    }

    [Fact]
    public async Task RagQueryJob_OnSuccess_PersistsAnswerAndCitationsOnJob()
    {
        var ws = await SeedWorkspaceAsync();
        var company = TestData.Company(ws.Workspace, "Acme Rockets");
        var deal = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne, company);
        var activity = TestData.Activity(ws.Workspace, deal, "call", "Discussed pricing tiers with the Acme team.");
        await WithDb(async db =>
        {
            db.Companies.Add(company);
            db.Deals.Add(deal);
            db.Activities.Add(activity);
            await db.SaveChangesAsync();
        });

        Anthropic.NextResponse = _ => FakeAnthropicMessagesClient.ToolUseMessage("answer_question", new System.Text.Json.Nodes.JsonObject
        {
            ["answer"] = "Pricing was discussed on a call.",
            ["citedActivityIndexes"] = new System.Text.Json.Nodes.JsonArray(0),
        });
        try
        {
            var askResponse = await ws.Client.PostAsJsonAsync("/api/ask", new AskQuestionRequest("what have we discussed with Acme about pricing", null, null, null));
            askResponse.EnsureSuccessStatusCode();
            var enqueued = await askResponse.Content.ReadFromJsonAsync<AskQuestionResponse>();
            Assert.NotNull(enqueued);

            await ProcessAllPendingJobsAsync();

            var statusResponse = await ws.Client.GetFromJsonAsync<JobStatusResponse>($"/api/jobs/{enqueued!.JobId}");
            Assert.NotNull(statusResponse);
            Assert.Equal("succeeded", statusResponse!.Status);
            Assert.NotNull(statusResponse.Result);
            // The frontend parses Job.Result as plain JSON expecting
            // camelCase keys — a case-sensitive check, not just
            // deserialize-anything.
            Assert.Contains("\"answer\":", statusResponse.Result);
            Assert.Contains("\"citations\":", statusResponse.Result);

            var result = JsonSerializer.Deserialize<RagAnswerDto>(statusResponse.Result!, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert.Equal("Pricing was discussed on a call.", result!.Answer);
            var citation = Assert.Single(result.Citations);
            Assert.Equal(activity.Id, citation.ActivityId);
        }
        finally
        {
            Anthropic.NextResponse = null;
        }
    }
}
