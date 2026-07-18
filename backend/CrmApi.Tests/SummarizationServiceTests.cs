using CrmApi.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CrmApi.Tests;

[Collection(CrmApiCollection.Name)]
public class SummarizationServiceTests(CrmApiFactory factory) : IntegrationTestBase(factory)
{
    private async Task<SummaryResult> SummarizeAsync(string dealId, string workspaceId)
    {
        using var scope = Factory.Services.CreateScope();
        var summarization = scope.ServiceProvider.GetRequiredService<SummarizationService>();
        return await summarization.SummarizeDeal(dealId, workspaceId);
    }

    [Fact]
    public async Task SummarizeDeal_FirstTime_CallsModelAndPersists()
    {
        var ws = await SeedWorkspaceAsync();
        var deal = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne);
        await WithDb(async db =>
        {
            db.Deals.Add(deal);
            db.Activities.Add(TestData.Activity(ws.Workspace, deal, "call", "Intro call went well"));
            await db.SaveChangesAsync();
        });

        Anthropic.NextResponse = _ => FakeAnthropicMessagesClient.DefaultSummaryMessage("Deal is off to a good start.");
        try
        {
            var result = await SummarizeAsync(deal.Id, ws.Workspace.Id);

            Assert.Equal("Deal is off to a good start.", result.Summary);
            Assert.False(result.UsedCache);

            var persisted = await WithDb(db => db.Deals.SingleAsync(d => d.Id == deal.Id));
            Assert.Equal("Deal is off to a good start.", persisted.AiSummary);
            Assert.NotNull(persisted.AiSummarizedAt);
        }
        finally
        {
            Anthropic.NextResponse = null;
        }
    }

    [Fact]
    public async Task SummarizeDeal_WithNoNewActivitySinceLastSummary_ReturnsCacheWithoutCallingModel()
    {
        var ws = await SeedWorkspaceAsync();
        var deal = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne);
        deal.AiSummary = "Existing cached summary.";
        deal.AiSummarizedAt = DateTime.UtcNow;
        await WithDb(async db => { db.Deals.Add(deal); await db.SaveChangesAsync(); });

        var callCountBefore = Anthropic.CallCount;
        var result = await SummarizeAsync(deal.Id, ws.Workspace.Id);

        Assert.Equal("Existing cached summary.", result.Summary);
        Assert.True(result.UsedCache);
        Assert.Equal(callCountBefore, Anthropic.CallCount); // cache hit must never reach anthropic.Create
    }

    [Fact]
    public async Task SummarizeDeal_WithNewActivitySinceLastSummary_FoldsInAndUpdatesCache()
    {
        var ws = await SeedWorkspaceAsync();
        var deal = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne);
        deal.AiSummary = "Old summary.";
        deal.AiSummarizedAt = DateTime.UtcNow.AddDays(-2);
        await WithDb(async db =>
        {
            db.Deals.Add(deal);
            db.Activities.Add(TestData.Activity(ws.Workspace, deal, "email", "Sent updated proposal"));
            await db.SaveChangesAsync();
        });

        Anthropic.NextResponse = _ => FakeAnthropicMessagesClient.DefaultSummaryMessage("Updated: proposal sent.");
        var callCountBefore = Anthropic.CallCount;
        try
        {
            var result = await SummarizeAsync(deal.Id, ws.Workspace.Id);

            Assert.Equal("Updated: proposal sent.", result.Summary);
            Assert.False(result.UsedCache);
            Assert.Equal(callCountBefore + 1, Anthropic.CallCount);

            var persisted = await WithDb(db => db.Deals.SingleAsync(d => d.Id == deal.Id));
            Assert.Equal("Updated: proposal sent.", persisted.AiSummary);
        }
        finally
        {
            Anthropic.NextResponse = null;
        }
    }

    [Fact]
    public async Task SummarizeDeal_ForUnknownDeal_ThrowsSummarizationFailedException()
    {
        var ws = await SeedWorkspaceAsync();

        await Assert.ThrowsAsync<SummarizationFailedException>(() => SummarizeAsync(Guid.NewGuid().ToString("N"), ws.Workspace.Id));
    }

    [Fact]
    public async Task SummarizeDeal_ForDealInAnotherWorkspace_ThrowsSummarizationFailedException()
    {
        var owner = await SeedWorkspaceAsync();
        var intruder = await SeedWorkspaceAsync();
        var deal = TestData.Deal(owner.Workspace, owner.Pipeline, owner.StageOne);
        await WithDb(async db => { db.Deals.Add(deal); await db.SaveChangesAsync(); });

        await Assert.ThrowsAsync<SummarizationFailedException>(() => SummarizeAsync(deal.Id, intruder.Workspace.Id));
    }

    [Fact]
    public async Task SummarizeDeal_WhenModelDoesNotCallTheTool_ThrowsSummarizationFailedException()
    {
        var ws = await SeedWorkspaceAsync();
        var deal = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne);
        await WithDb(async db => { db.Deals.Add(deal); await db.SaveChangesAsync(); });

        Anthropic.NextResponse = _ => FakeAnthropicMessagesClient.TextOnlyMessage();
        try
        {
            await Assert.ThrowsAsync<SummarizationFailedException>(() => SummarizeAsync(deal.Id, ws.Workspace.Id));
        }
        finally
        {
            Anthropic.NextResponse = null;
        }
    }

    [Fact]
    public async Task SummarizeDeal_WhenAnthropicCallThrows_ThrowsSummarizationFailedException()
    {
        var ws = await SeedWorkspaceAsync();
        var deal = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne);
        await WithDb(async db => { db.Deals.Add(deal); await db.SaveChangesAsync(); });

        Anthropic.NextException = new HttpRequestException("simulated network failure");
        try
        {
            await Assert.ThrowsAsync<SummarizationFailedException>(() => SummarizeAsync(deal.Id, ws.Workspace.Id));
        }
        finally
        {
            Anthropic.NextException = null;
        }
    }
}
