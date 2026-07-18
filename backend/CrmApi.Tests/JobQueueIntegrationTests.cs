using System.Net.Http.Json;
using CrmApi.Dtos;
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
}
