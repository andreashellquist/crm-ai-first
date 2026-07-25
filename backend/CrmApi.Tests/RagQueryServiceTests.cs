using CrmApi.Models;
using CrmApi.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CrmApi.Tests;

// Exercises RagQueryService directly (not through the job queue) — same
// rationale as EmailDraftingServiceTests/DealScoringServiceTests.
[Collection(CrmApiCollection.Name)]
public class RagQueryServiceTests(CrmApiFactory factory) : IntegrationTestBase(factory)
{
    private async Task<RagQueryResult> AskAsync(string question, string workspaceId, string? dealId = null, string? contactId = null, string? companyId = null)
    {
        using var scope = Factory.Services.CreateScope();
        var rag = scope.ServiceProvider.GetRequiredService<RagQueryService>();
        return await rag.AskQuestion(question, workspaceId, dealId, contactId, companyId);
    }

    [Fact]
    public async Task AskQuestion_MatchesActivityByCompanyNameKeyword_AndCitesIt()
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

        Anthropic.NextResponse = _ => ToolUseMessage("Pricing was discussed on a call.", [0]);
        try
        {
            var result = await AskAsync("what have we discussed with Acme about pricing", ws.Workspace.Id);

            Assert.Equal("Pricing was discussed on a call.", result.Answer);
            var citation = Assert.Single(result.Citations);
            Assert.Equal(activity.Id, citation.ActivityId);
            Assert.Equal(deal.Id, citation.DealId);
        }
        finally
        {
            Anthropic.NextResponse = null;
        }
    }

    [Fact]
    public async Task AskQuestion_NeverRetrievesAnotherWorkspacesActivities()
    {
        var owner = await SeedWorkspaceAsync();
        var intruder = await SeedWorkspaceAsync();
        var company = TestData.Company(owner.Workspace, "Globex Secrets");
        var deal = TestData.Deal(owner.Workspace, owner.Pipeline, owner.StageOne, company);
        var activity = TestData.Activity(owner.Workspace, deal, "note", "Confidential Globex negotiation details.");
        await WithDb(async db =>
        {
            db.Companies.Add(company);
            db.Deals.Add(deal);
            db.Activities.Add(activity);
            await db.SaveChangesAsync();
        });

        Anthropic.NextResponse = _ => ToolUseMessage("No matching activity found.", []);
        try
        {
            var result = await AskAsync("what did we discuss with Globex", intruder.Workspace.Id);

            Assert.Empty(result.Citations);
            // The other workspace's confidential activity text must never
            // even reach the prompt sent to Claude, not just be uncited.
            var sentPrompt = System.Text.Json.JsonSerializer.Serialize(Anthropic.LastRequest);
            Assert.DoesNotContain("Confidential Globex negotiation details", sentPrompt);
        }
        finally
        {
            Anthropic.NextResponse = null;
        }
    }

    [Fact]
    public async Task AskQuestion_ScopedToADeal_IgnoresKeywordsAndUsesOnlyThatDealsActivities()
    {
        var ws = await SeedWorkspaceAsync();
        var companyA = TestData.Company(ws.Workspace, "Alpha Inc");
        var companyB = TestData.Company(ws.Workspace, "Beta Inc");
        var dealA = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne, companyA);
        var dealB = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne, companyB);
        var activityA = TestData.Activity(ws.Workspace, dealA, "note", "Alpha deal notes.");
        var activityB = TestData.Activity(ws.Workspace, dealB, "note", "Beta deal notes, mentions Alpha as a competitor.");
        await WithDb(async db =>
        {
            db.Companies.AddRange(companyA, companyB);
            db.Deals.AddRange(dealA, dealB);
            db.Activities.AddRange(activityA, activityB);
            await db.SaveChangesAsync();
        });

        Anthropic.NextResponse = _ => ToolUseMessage("Alpha deal notes.", [0]);
        try
        {
            var result = await AskAsync("what's the status", ws.Workspace.Id, dealId: dealA.Id);

            var citation = Assert.Single(result.Citations);
            Assert.Equal(activityA.Id, citation.ActivityId);
        }
        finally
        {
            Anthropic.NextResponse = null;
        }
    }

    [Fact]
    public async Task AskQuestion_NoMatchingActivities_StillCallsModelWithEmptyContextAndReturnsAnswer()
    {
        var ws = await SeedWorkspaceAsync();

        Anthropic.NextResponse = _ => ToolUseMessage("I don't have any activity on that in this workspace.", []);
        try
        {
            var result = await AskAsync("what have we discussed with Nonexistent Corp", ws.Workspace.Id);

            Assert.Equal("I don't have any activity on that in this workspace.", result.Answer);
            Assert.Empty(result.Citations);
        }
        finally
        {
            Anthropic.NextResponse = null;
        }
    }

    [Fact]
    public async Task AskQuestion_WhenModelDoesNotCallTheTool_ThrowsRagQueryFailedException()
    {
        var ws = await SeedWorkspaceAsync();

        Anthropic.NextResponse = _ => FakeAnthropicMessagesClient.TextOnlyMessage();
        try
        {
            await Assert.ThrowsAsync<RagQueryFailedException>(() => AskAsync("anything", ws.Workspace.Id));
        }
        finally
        {
            Anthropic.NextResponse = null;
        }
    }

    [Fact]
    public async Task AskQuestion_WhenAnthropicCallThrows_ThrowsRagQueryFailedException()
    {
        var ws = await SeedWorkspaceAsync();

        Anthropic.NextException = new HttpRequestException("simulated network failure");
        try
        {
            await Assert.ThrowsAsync<RagQueryFailedException>(() => AskAsync("anything", ws.Workspace.Id));
        }
        finally
        {
            Anthropic.NextException = null;
        }
    }

    private static Anthropic.Models.Messages.Message ToolUseMessage(string answer, int[] citedActivityIndexes) =>
        FakeAnthropicMessagesClient.ToolUseMessage("answer_question", new System.Text.Json.Nodes.JsonObject
        {
            ["answer"] = answer,
            ["citedActivityIndexes"] = new System.Text.Json.Nodes.JsonArray(citedActivityIndexes.Select(i => (System.Text.Json.Nodes.JsonNode)i).ToArray()),
        });
}
