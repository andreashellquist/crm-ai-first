using System.Net.Http.Json;
using System.Text.Json;
using CrmApi.Dtos;
using CrmApi.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CrmApi.Tests;

// Exercises DealScoringService directly (not through the job queue) so
// validation-error paths don't have to wait out JobWorker's retry backoff.
// See CrmApi/Services/IAnthropicMessagesClient.cs for why the client is
// injectable at all.
[Collection(CrmApiCollection.Name)]
public class DealScoringServiceTests(CrmApiFactory factory) : IntegrationTestBase(factory)
{
    private async Task<ScoreResult> ScoreAsync(string dealId, string workspaceId)
    {
        using var scope = Factory.Services.CreateScope();
        var scoring = scope.ServiceProvider.GetRequiredService<DealScoringService>();
        return await scoring.ScoreDeal(dealId, workspaceId);
    }

    [Fact]
    public async Task ScoreDeal_WithValidToolResponse_PersistsScoreOnDeal()
    {
        var ws = await SeedWorkspaceAsync();
        var deal = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne);
        await WithDb(async db => { db.Deals.Add(deal); await db.SaveChangesAsync(); });

        Anthropic.NextResponse = _ => FakeAnthropicMessagesClient.DefaultScoreMessage(score: 83, rationale: "Strong signals.");
        try
        {
            var result = await ScoreAsync(deal.Id, ws.Workspace.Id);

            Assert.Equal(83, result.Score);
            Assert.Equal("Strong signals.", result.Rationale);
            Assert.Equal(2, result.Signals.Count);

            var persisted = await WithDb(db => db.Deals.SingleAsync(d => d.Id == deal.Id));
            Assert.Equal(83, persisted.AiScore);
            Assert.Equal("Strong signals.", persisted.AiScoreRationale);
            Assert.NotNull(persisted.AiScoredAt);
        }
        finally
        {
            Anthropic.NextResponse = null;
        }
    }

    [Fact]
    public async Task ScoreDeal_UsesWorkspaceTerminologyInPrompt()
    {
        var ws = await SeedWorkspaceAsync();
        var deal = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne);
        await WithDb(async db => { db.Deals.Add(deal); await db.SaveChangesAsync(); });

        var terminology = new Dictionary<string, JsonElement>
        {
            ["deal"] = JsonSerializer.SerializeToElement(new { singular = "Listing", plural = "Listings" }),
        };
        var settingsResponse = await ws.Client.PutAsJsonAsync("/api/workspace/settings", new UpdateWorkspaceSettingsRequest(terminology, null));
        settingsResponse.EnsureSuccessStatusCode();

        Anthropic.NextResponse = _ => FakeAnthropicMessagesClient.DefaultScoreMessage();
        try
        {
            await ScoreAsync(deal.Id, ws.Workspace.Id);

            var request = Anthropic.LastRequest;
            Assert.NotNull(request);
            Assert.Contains("Listing", (string)request!.System!.Value!);
        }
        finally
        {
            Anthropic.NextResponse = null;
        }
    }

    [Fact]
    public async Task ScoreDeal_WithNoWorkspaceSettings_FallsBackToCanonicalTerm()
    {
        var ws = await SeedWorkspaceAsync();
        var deal = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne);
        await WithDb(async db => { db.Deals.Add(deal); await db.SaveChangesAsync(); });

        Anthropic.NextResponse = _ => FakeAnthropicMessagesClient.DefaultScoreMessage();
        try
        {
            await ScoreAsync(deal.Id, ws.Workspace.Id);

            var request = Anthropic.LastRequest;
            Assert.NotNull(request);
            Assert.Contains("deals", (string)request!.System!.Value!);
        }
        finally
        {
            Anthropic.NextResponse = null;
        }
    }

    [Fact]
    public async Task ScoreDeal_ForUnknownDeal_ThrowsDealNotFoundException()
    {
        var ws = await SeedWorkspaceAsync();

        await Assert.ThrowsAsync<DealNotFoundException>(() => ScoreAsync(Guid.NewGuid().ToString("N"), ws.Workspace.Id));
    }

    [Fact]
    public async Task ScoreDeal_ForDealInAnotherWorkspace_ThrowsDealNotFoundException()
    {
        var owner = await SeedWorkspaceAsync();
        var intruder = await SeedWorkspaceAsync();
        var deal = TestData.Deal(owner.Workspace, owner.Pipeline, owner.StageOne);
        await WithDb(async db => { db.Deals.Add(deal); await db.SaveChangesAsync(); });

        await Assert.ThrowsAsync<DealNotFoundException>(() => ScoreAsync(deal.Id, intruder.Workspace.Id));
    }

    [Fact]
    public async Task ScoreDeal_WhenModelDoesNotCallTheTool_ThrowsScoringFailedException()
    {
        var ws = await SeedWorkspaceAsync();
        var deal = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne);
        await WithDb(async db => { db.Deals.Add(deal); await db.SaveChangesAsync(); });

        Anthropic.NextResponse = _ => FakeAnthropicMessagesClient.TextOnlyMessage();
        try
        {
            await Assert.ThrowsAsync<ScoringFailedException>(() => ScoreAsync(deal.Id, ws.Workspace.Id));

            var persisted = await WithDb(db => db.Deals.SingleAsync(d => d.Id == deal.Id));
            Assert.Null(persisted.AiScore);
        }
        finally
        {
            Anthropic.NextResponse = null;
        }
    }

    [Fact]
    public async Task ScoreDeal_WhenScoreIsOutOfRange_ThrowsScoringFailedException()
    {
        var ws = await SeedWorkspaceAsync();
        var deal = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne);
        await WithDb(async db => { db.Deals.Add(deal); await db.SaveChangesAsync(); });

        Anthropic.NextResponse = _ => FakeAnthropicMessagesClient.DefaultScoreMessage(score: 150);
        try
        {
            await Assert.ThrowsAsync<ScoringFailedException>(() => ScoreAsync(deal.Id, ws.Workspace.Id));
        }
        finally
        {
            Anthropic.NextResponse = null;
        }
    }

    [Fact]
    public async Task ScoreDeal_WhenAnthropicCallThrows_ThrowsScoringFailedException()
    {
        var ws = await SeedWorkspaceAsync();
        var deal = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne);
        await WithDb(async db => { db.Deals.Add(deal); await db.SaveChangesAsync(); });

        Anthropic.NextException = new HttpRequestException("simulated network failure");
        try
        {
            await Assert.ThrowsAsync<ScoringFailedException>(() => ScoreAsync(deal.Id, ws.Workspace.Id));
        }
        finally
        {
            Anthropic.NextException = null;
        }
    }
}
