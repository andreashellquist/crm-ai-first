using System.Net;
using System.Net.Http.Json;
using CrmApi.Dtos;

namespace CrmApi.Tests;

[Collection(CrmApiCollection.Name)]
public class AskControllerTests(CrmApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Ask_WithBlankQuestion_ReturnsBadRequest()
    {
        var ws = await SeedWorkspaceAsync();

        var response = await ws.Client.PostAsJsonAsync("/api/ask", new AskQuestionRequest("   ", null, null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Ask_WithMoreThanOneScopeHint_ReturnsBadRequest()
    {
        var ws = await SeedWorkspaceAsync();
        var deal = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne);
        var contact = TestData.Contact(ws.Workspace);
        await WithDb(async db =>
        {
            db.Deals.Add(deal);
            db.Contacts.Add(contact);
            await db.SaveChangesAsync();
        });

        var response = await ws.Client.PostAsJsonAsync("/api/ask", new AskQuestionRequest("what's up", deal.Id, contact.Id, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Ask_ScopedToADealInAnotherWorkspace_ReturnsNotFound()
    {
        var owner = await SeedWorkspaceAsync();
        var intruder = await SeedWorkspaceAsync();
        var deal = TestData.Deal(owner.Workspace, owner.Pipeline, owner.StageOne);
        await WithDb(async db => { db.Deals.Add(deal); await db.SaveChangesAsync(); });

        var response = await intruder.Client.PostAsJsonAsync("/api/ask", new AskQuestionRequest("what's up", deal.Id, null, null));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Ask_ScopedToAnUnknownContact_ReturnsNotFound()
    {
        var ws = await SeedWorkspaceAsync();

        var response = await ws.Client.PostAsJsonAsync("/api/ask", new AskQuestionRequest("what's up", null, Guid.NewGuid().ToString("N"), null));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Ask_ScopedToAnUnknownCompany_ReturnsNotFound()
    {
        var ws = await SeedWorkspaceAsync();

        var response = await ws.Client.PostAsJsonAsync("/api/ask", new AskQuestionRequest("what's up", null, null, Guid.NewGuid().ToString("N")));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Ask_WithNoScope_EnqueuesJobAndReturnsJobId()
    {
        var ws = await SeedWorkspaceAsync();

        var response = await ws.Client.PostAsJsonAsync("/api/ask", new AskQuestionRequest("what's the latest on our deals", null, null, null));

        response.EnsureSuccessStatusCode();
        var enqueued = await response.Content.ReadFromJsonAsync<AskQuestionResponse>();
        Assert.NotNull(enqueued);
        Assert.NotEmpty(enqueued!.JobId);
    }
}
