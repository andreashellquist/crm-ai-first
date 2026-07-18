using CrmApi.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CrmApi.Tests;

// Exercises EmailDraftingService directly (not through the job queue) — same
// rationale as DealScoringServiceTests: validation-error paths don't have to
// wait out JobWorker's retry backoff.
[Collection(CrmApiCollection.Name)]
public class EmailDraftingServiceTests(CrmApiFactory factory) : IntegrationTestBase(factory)
{
    private async Task<EmailDraftResult> DraftAsync(string dealId, string workspaceId, string? instruction = null)
    {
        using var scope = Factory.Services.CreateScope();
        var drafting = scope.ServiceProvider.GetRequiredService<EmailDraftingService>();
        return await drafting.DraftEmail(dealId, workspaceId, instruction);
    }

    [Fact]
    public async Task DraftEmail_WithValidToolResponse_ReturnsSubjectAndBody()
    {
        var ws = await SeedWorkspaceAsync();
        var contact = TestData.Contact(ws.Workspace, "Jane");
        var deal = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne);
        deal.Contacts.Add(contact);
        await WithDb(async db =>
        {
            db.Contacts.Add(contact);
            db.Deals.Add(deal);
            await db.SaveChangesAsync();
        });

        Anthropic.NextResponse = _ => FakeAnthropicMessagesClient.DefaultDraftMessage("Following up", "Hi Jane, checking in.");
        try
        {
            var result = await DraftAsync(deal.Id, ws.Workspace.Id, "keep it short");

            Assert.Equal("Following up", result.Subject);
            Assert.Equal("Hi Jane, checking in.", result.Body);
        }
        finally
        {
            Anthropic.NextResponse = null;
        }
    }

    [Fact]
    public async Task DraftEmail_ForUnknownDeal_ThrowsDraftingFailedException()
    {
        var ws = await SeedWorkspaceAsync();

        await Assert.ThrowsAsync<DraftingFailedException>(() => DraftAsync(Guid.NewGuid().ToString("N"), ws.Workspace.Id));
    }

    [Fact]
    public async Task DraftEmail_ForDealInAnotherWorkspace_ThrowsDraftingFailedException()
    {
        var owner = await SeedWorkspaceAsync();
        var intruder = await SeedWorkspaceAsync();
        var deal = TestData.Deal(owner.Workspace, owner.Pipeline, owner.StageOne);
        await WithDb(async db => { db.Deals.Add(deal); await db.SaveChangesAsync(); });

        await Assert.ThrowsAsync<DraftingFailedException>(() => DraftAsync(deal.Id, intruder.Workspace.Id));
    }

    [Fact]
    public async Task DraftEmail_WhenModelDoesNotCallTheTool_ThrowsDraftingFailedException()
    {
        var ws = await SeedWorkspaceAsync();
        var deal = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne);
        await WithDb(async db => { db.Deals.Add(deal); await db.SaveChangesAsync(); });

        Anthropic.NextResponse = _ => FakeAnthropicMessagesClient.TextOnlyMessage();
        try
        {
            await Assert.ThrowsAsync<DraftingFailedException>(() => DraftAsync(deal.Id, ws.Workspace.Id));
        }
        finally
        {
            Anthropic.NextResponse = null;
        }
    }

    [Fact]
    public async Task DraftEmail_WhenAnthropicCallThrows_ThrowsDraftingFailedException()
    {
        var ws = await SeedWorkspaceAsync();
        var deal = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne);
        await WithDb(async db => { db.Deals.Add(deal); await db.SaveChangesAsync(); });

        Anthropic.NextException = new HttpRequestException("simulated network failure");
        try
        {
            await Assert.ThrowsAsync<DraftingFailedException>(() => DraftAsync(deal.Id, ws.Workspace.Id));
        }
        finally
        {
            Anthropic.NextException = null;
        }
    }

    [Fact]
    public async Task DraftEmail_UsesWorkspaceTerminologyInToolDescription()
    {
        var ws = await SeedWorkspaceAsync();
        var deal = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne);
        await WithDb(async db => { db.Deals.Add(deal); await db.SaveChangesAsync(); });

        Anthropic.NextResponse = _ => FakeAnthropicMessagesClient.DefaultDraftMessage();
        try
        {
            await DraftAsync(deal.Id, ws.Workspace.Id);

            var request = Anthropic.LastRequest;
            Assert.NotNull(request);
            var tool = request!.Tools!.Select(t => t.Value).OfType<Anthropic.Models.Messages.Tool>().Single();
            Assert.Contains("deal", tool.Description);
        }
        finally
        {
            Anthropic.NextResponse = null;
        }
    }
}
