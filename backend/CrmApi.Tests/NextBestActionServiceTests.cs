using CrmApi.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CrmApi.Tests;

[Collection(CrmApiCollection.Name)]
public class NextBestActionServiceTests(CrmApiFactory factory) : IntegrationTestBase(factory)
{
    private async Task<NextBestActionResult> SuggestAsync(string dealId, string workspaceId)
    {
        using var scope = Factory.Services.CreateScope();
        var nextBestAction = scope.ServiceProvider.GetRequiredService<NextBestActionService>();
        return await nextBestAction.SuggestActions(dealId, workspaceId);
    }

    [Fact]
    public async Task SuggestActions_WhenModelCallsFinalToolImmediately_ReturnsSuggestions()
    {
        var ws = await SeedWorkspaceAsync();
        var deal = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne);
        await WithDb(async db => { db.Deals.Add(deal); await db.SaveChangesAsync(); });

        Anthropic.NextResponse = _ => FakeAnthropicMessagesClient.DefaultSuggestActionsMessage(
            ("Send a pricing follow-up", "No contact in over a week.", "high"));
        try
        {
            var result = await SuggestAsync(deal.Id, ws.Workspace.Id);

            var suggestion = Assert.Single(result.Suggestions);
            Assert.Equal("Send a pricing follow-up", suggestion.Action);
            Assert.Equal("high", suggestion.Confidence);
        }
        finally
        {
            Anthropic.NextResponse = null;
        }
    }

    [Fact]
    public async Task SuggestActions_WhenModelCallsReadOnlyToolsFirst_ExecutesThemThenReturnsFinalSuggestions()
    {
        var ws = await SeedWorkspaceAsync();
        var deal = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne);
        await WithDb(async db =>
        {
            db.Deals.Add(deal);
            db.Activities.Add(TestData.Activity(ws.Workspace, deal, "call", "Discussed pricing"));
            await db.SaveChangesAsync();
        });

        var turn = 0;
        Anthropic.NextResponse = _ =>
        {
            turn++;
            return turn switch
            {
                1 => FakeAnthropicMessagesClient.ReadOnlyToolCallMessage("get_deal_details"),
                2 => FakeAnthropicMessagesClient.ReadOnlyToolCallMessage("list_recent_activities"),
                _ => FakeAnthropicMessagesClient.DefaultSuggestActionsMessage(
                    ("Follow up on pricing discussion", "Last call covered pricing but no follow-up logged.", "medium")),
            };
        };
        try
        {
            var result = await SuggestAsync(deal.Id, ws.Workspace.Id);

            var suggestion = Assert.Single(result.Suggestions);
            Assert.Equal("Follow up on pricing discussion", suggestion.Action);
            Assert.Equal(3, turn); // two read-only turns + the final suggest_actions turn
        }
        finally
        {
            Anthropic.NextResponse = null;
        }
    }

    [Fact]
    public async Task SuggestActions_WhenModelNeverCallsFinalTool_ThrowsAfterMaxTurns()
    {
        var ws = await SeedWorkspaceAsync();
        var deal = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne);
        await WithDb(async db => { db.Deals.Add(deal); await db.SaveChangesAsync(); });

        Anthropic.NextResponse = _ => FakeAnthropicMessagesClient.ReadOnlyToolCallMessage("get_deal_details");
        try
        {
            await Assert.ThrowsAsync<NextBestActionFailedException>(() => SuggestAsync(deal.Id, ws.Workspace.Id));
        }
        finally
        {
            Anthropic.NextResponse = null;
        }
    }

    [Fact]
    public async Task SuggestActions_WithInvalidConfidenceValue_ThrowsNextBestActionFailedException()
    {
        var ws = await SeedWorkspaceAsync();
        var deal = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne);
        await WithDb(async db => { db.Deals.Add(deal); await db.SaveChangesAsync(); });

        Anthropic.NextResponse = _ => FakeAnthropicMessagesClient.DefaultSuggestActionsMessage(
            ("Do something", "Because reasons.", "extremely-confident"));
        try
        {
            await Assert.ThrowsAsync<NextBestActionFailedException>(() => SuggestAsync(deal.Id, ws.Workspace.Id));
        }
        finally
        {
            Anthropic.NextResponse = null;
        }
    }

    [Fact]
    public async Task SuggestActions_ForUnknownDeal_ThrowsNextBestActionFailedException()
    {
        var ws = await SeedWorkspaceAsync();

        await Assert.ThrowsAsync<NextBestActionFailedException>(() => SuggestAsync(Guid.NewGuid().ToString("N"), ws.Workspace.Id));
    }

    [Fact]
    public async Task SuggestActions_ForDealInAnotherWorkspace_ThrowsNextBestActionFailedException()
    {
        var owner = await SeedWorkspaceAsync();
        var intruder = await SeedWorkspaceAsync();
        var deal = TestData.Deal(owner.Workspace, owner.Pipeline, owner.StageOne);
        await WithDb(async db => { db.Deals.Add(deal); await db.SaveChangesAsync(); });

        await Assert.ThrowsAsync<NextBestActionFailedException>(() => SuggestAsync(deal.Id, intruder.Workspace.Id));
    }

    [Fact]
    public async Task SuggestActions_WhenAnthropicCallThrows_ThrowsNextBestActionFailedException()
    {
        var ws = await SeedWorkspaceAsync();
        var deal = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne);
        await WithDb(async db => { db.Deals.Add(deal); await db.SaveChangesAsync(); });

        Anthropic.NextException = new HttpRequestException("simulated network failure");
        try
        {
            await Assert.ThrowsAsync<NextBestActionFailedException>(() => SuggestAsync(deal.Id, ws.Workspace.Id));
        }
        finally
        {
            Anthropic.NextException = null;
        }
    }
}
