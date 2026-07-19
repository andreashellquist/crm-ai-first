using System.Net;
using System.Net.Http.Json;
using CrmApi.Dtos;
using CrmApi.Models;
using Microsoft.EntityFrameworkCore;

namespace CrmApi.Tests;

// The single highest-priority test category for this app: a caller
// authenticated for workspace A must never be able to read or mutate
// workspace B's data, even when it supplies a workspace-B id it obtained
// some other way (e.g. guessed, leaked in a log, or from an old session).
// Every controller re-scopes its EF Core queries by CurrentUser.WorkspaceId
// (see backend-api-engineer) — these tests are the regression guard for that.
[Collection(CrmApiCollection.Name)]
public class MultiTenantIsolationTests(CrmApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Contacts_List_OnlyReturnsCallersWorkspace()
    {
        var ws1 = await SeedWorkspaceAsync();
        var ws2 = await SeedWorkspaceAsync();

        var ws1Contact = TestData.Contact(ws1.Workspace, "Alice");
        var ws2Contact = TestData.Contact(ws2.Workspace, "Bob");
        await WithDb(async db =>
        {
            db.Contacts.AddRange(ws1Contact, ws2Contact);
            await db.SaveChangesAsync();
        });

        var contacts = await ws1.Client.GetFromJsonAsync<List<ContactDto>>("/api/contacts");

        Assert.NotNull(contacts);
        Assert.Contains(contacts!, c => c.Id == ws1Contact.Id);
        Assert.DoesNotContain(contacts!, c => c.Id == ws2Contact.Id);
    }

    [Fact]
    public async Task Pipeline_Board_DoesNotLeakOtherWorkspacesPipeline()
    {
        var ws1 = await SeedWorkspaceAsync();
        var ws2 = await SeedWorkspaceAsync();

        var board = await ws1.Client.GetFromJsonAsync<PipelineBoardDto>("/api/pipeline");

        Assert.NotNull(board);
        Assert.Equal(ws1.Pipeline.Id, board!.Id);
        Assert.NotEqual(ws2.Pipeline.Id, board.Id);
    }

    [Fact]
    public async Task GetDeal_ForAnotherWorkspacesDeal_ReturnsNotFound()
    {
        var owner = await SeedWorkspaceAsync();
        var intruder = await SeedWorkspaceAsync();

        var deal = TestData.Deal(owner.Workspace, owner.Pipeline, owner.StageOne);
        await WithDb(async db =>
        {
            db.Deals.Add(deal);
            await db.SaveChangesAsync();
        });

        var response = await intruder.Client.GetAsync($"/api/deals/{deal.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task MoveDeal_ForAnotherWorkspacesDeal_ReturnsNotFoundAndDoesNotMove()
    {
        var owner = await SeedWorkspaceAsync();
        var intruder = await SeedWorkspaceAsync();

        var deal = TestData.Deal(owner.Workspace, owner.Pipeline, owner.StageOne);
        await WithDb(async db =>
        {
            db.Deals.Add(deal);
            await db.SaveChangesAsync();
        });

        // Intruder supplies its own valid stage id — the deal id is the only
        // thing scoped to the wrong workspace. Controller must still reject.
        var response = await intruder.Client.PostAsJsonAsync($"/api/deals/{deal.Id}/move", new MoveDealRequest(intruder.StageTwo.Id));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var stageIdAfter = await WithDb(async db => (await db.Deals.FindAsync(deal.Id))!.StageId);
        Assert.Equal(owner.StageOne.Id, stageIdAfter);
    }

    [Fact]
    public async Task MoveDeal_ToAnotherWorkspacesStage_ReturnsNotFoundAndDoesNotMove()
    {
        var owner = await SeedWorkspaceAsync();
        var otherWorkspace = await SeedWorkspaceAsync();

        var deal = TestData.Deal(owner.Workspace, owner.Pipeline, owner.StageOne);
        await WithDb(async db =>
        {
            db.Deals.Add(deal);
            await db.SaveChangesAsync();
        });

        // Owner's own deal, but the target stage id belongs to a different
        // workspace's pipeline — must not be usable as a move target either.
        var response = await owner.Client.PostAsJsonAsync($"/api/deals/{deal.Id}/move", new MoveDealRequest(otherWorkspace.StageTwo.Id));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var stageIdAfter = await WithDb(async db => (await db.Deals.FindAsync(deal.Id))!.StageId);
        Assert.Equal(owner.StageOne.Id, stageIdAfter);
    }

    [Fact]
    public async Task LogActivity_ForAnotherWorkspacesDeal_ReturnsNotFound()
    {
        var owner = await SeedWorkspaceAsync();
        var intruder = await SeedWorkspaceAsync();

        var deal = TestData.Deal(owner.Workspace, owner.Pipeline, owner.StageOne);
        await WithDb(async db =>
        {
            db.Deals.Add(deal);
            await db.SaveChangesAsync();
        });

        var response = await intruder.Client.PostAsJsonAsync($"/api/deals/{deal.Id}/activities", new LogActivityRequest("note", "snooping"));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ScoreDeal_ForAnotherWorkspacesDeal_ReturnsNotFound()
    {
        var owner = await SeedWorkspaceAsync();
        var intruder = await SeedWorkspaceAsync();

        var deal = TestData.Deal(owner.Workspace, owner.Pipeline, owner.StageOne);
        await WithDb(async db =>
        {
            db.Deals.Add(deal);
            await db.SaveChangesAsync();
        });

        var response = await intruder.Client.PostAsync($"/api/deals/{deal.Id}/score", content: null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DraftEmail_ForAnotherWorkspacesDeal_ReturnsNotFound()
    {
        var owner = await SeedWorkspaceAsync();
        var intruder = await SeedWorkspaceAsync();

        var deal = TestData.Deal(owner.Workspace, owner.Pipeline, owner.StageOne);
        await WithDb(async db =>
        {
            db.Deals.Add(deal);
            await db.SaveChangesAsync();
        });

        var response = await intruder.Client.PostAsJsonAsync($"/api/deals/{deal.Id}/draft-email", new DraftEmailRequest(null));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SummarizeDeal_ForAnotherWorkspacesDeal_ReturnsNotFound()
    {
        var owner = await SeedWorkspaceAsync();
        var intruder = await SeedWorkspaceAsync();

        var deal = TestData.Deal(owner.Workspace, owner.Pipeline, owner.StageOne);
        await WithDb(async db =>
        {
            db.Deals.Add(deal);
            await db.SaveChangesAsync();
        });

        var response = await intruder.Client.PostAsync($"/api/deals/{deal.Id}/summarize", content: null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task NextBestAction_ForAnotherWorkspacesDeal_ReturnsNotFound()
    {
        var owner = await SeedWorkspaceAsync();
        var intruder = await SeedWorkspaceAsync();

        var deal = TestData.Deal(owner.Workspace, owner.Pipeline, owner.StageOne);
        await WithDb(async db =>
        {
            db.Deals.Add(deal);
            await db.SaveChangesAsync();
        });

        var response = await intruder.Client.PostAsync($"/api/deals/{deal.Id}/next-best-action", content: null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ContactImport_NeverMatchesAnotherWorkspacesContactByEmail()
    {
        var owner = await SeedWorkspaceAsync();
        var intruder = await SeedWorkspaceAsync();

        var ownerContact = TestData.Contact(owner.Workspace, "OwnerName");
        ownerContact.Email = "shared@example.com";
        await WithDb(async db => { db.Contacts.Add(ownerContact); await db.SaveChangesAsync(); });

        var csv = "Email,First,Last\nshared@example.com,IntruderName,Doe";
        var mapping = new Dictionary<string, string> { ["email"] = "Email", ["firstName"] = "First", ["lastName"] = "Last" };
        var importResponse = await intruder.Client.PostAsJsonAsync("/api/contacts/import", new CsvImportRequest(csv, mapping));
        importResponse.EnsureSuccessStatusCode();
        var enqueued = await importResponse.Content.ReadFromJsonAsync<CsvImportResponse>();
        await ProcessAllPendingJobsAsync();

        var statusResponse = await intruder.Client.GetFromJsonAsync<JobStatusResponse>($"/api/jobs/{enqueued!.JobId}");
        Assert.Equal("succeeded", statusResponse!.Status);
        Assert.Contains("\"created\":1", statusResponse.Result); // created a new contact, did not match/update the owner's

        var ownerContactAfter = await WithDb(db => db.Contacts.SingleAsync(c => c.Id == ownerContact.Id));
        Assert.Equal("OwnerName", ownerContactAfter.FirstName); // untouched by the intruder's import

        var intruderContacts = await WithDb(db => db.Contacts.Where(c => c.WorkspaceId == intruder.Workspace.Id && c.Email == "shared@example.com").ToListAsync());
        Assert.Single(intruderContacts);
    }

    [Fact]
    public async Task Notifications_List_OnlyReturnsCallersWorkspace()
    {
        var owner = await SeedWorkspaceAsync();
        var intruder = await SeedWorkspaceAsync();
        await WithDb(async db =>
        {
            db.Notifications.Add(new Notification { WorkspaceId = owner.Workspace.Id, UserId = owner.User.Id, Type = "ai_suggestion_ready" });
            await db.SaveChangesAsync();
        });

        var notifications = await intruder.Client.GetFromJsonAsync<List<NotificationDto>>("/api/notifications");
        Assert.NotNull(notifications);
        Assert.Empty(notifications!);
    }

    [Fact]
    public async Task Search_OnlyReturnsCallersWorkspace()
    {
        var owner = await SeedWorkspaceAsync();
        var intruder = await SeedWorkspaceAsync();
        var contact = TestData.Contact(owner.Workspace, "Quillfeather");
        await WithDb(async db => { db.Contacts.Add(contact); await db.SaveChangesAsync(); });

        var result = await intruder.Client.GetFromJsonAsync<SearchResultsDto>("/api/search?q=quillfeather");

        Assert.NotNull(result);
        Assert.Empty(result!.Contacts);
    }

    [Fact]
    public async Task SavedViews_List_OnlyReturnsCallersWorkspace()
    {
        var owner = await SeedWorkspaceAsync();
        var intruder = await SeedWorkspaceAsync();
        await WithDb(async db =>
        {
            db.SavedViews.Add(new SavedView { WorkspaceId = owner.Workspace.Id, UserId = owner.User.Id, EntityType = "contact", Name = "Owner's view", QueryString = "q=a" });
            await db.SaveChangesAsync();
        });

        var views = await intruder.Client.GetFromJsonAsync<List<SavedViewDto>>("/api/saved-views?entityType=contact");

        Assert.NotNull(views);
        Assert.Empty(views!);
    }

    [Fact]
    public async Task JobStatus_ForAnotherWorkspacesJob_ReturnsNotFound()
    {
        var owner = await SeedWorkspaceAsync();
        var intruder = await SeedWorkspaceAsync();

        var deal = TestData.Deal(owner.Workspace, owner.Pipeline, owner.StageOne);
        await WithDb(async db =>
        {
            db.Deals.Add(deal);
            await db.SaveChangesAsync();
        });

        var scoreResponse = await owner.Client.PostAsync($"/api/deals/{deal.Id}/score", content: null);
        scoreResponse.EnsureSuccessStatusCode();
        var job = await scoreResponse.Content.ReadFromJsonAsync<ScoreDealResponse>();
        Assert.NotNull(job);
        // Drain it so it doesn't linger as a stray pending job that a later
        // test's batch scoops up along with whatever fake Anthropic
        // response happens to be configured at that point.
        await ProcessAllPendingJobsAsync();

        var ownerCanRead = await owner.Client.GetAsync($"/api/jobs/{job!.JobId}");
        Assert.Equal(HttpStatusCode.OK, ownerCanRead.StatusCode);

        var intruderCanRead = await intruder.Client.GetAsync($"/api/jobs/{job.JobId}");
        Assert.Equal(HttpStatusCode.NotFound, intruderCanRead.StatusCode);
    }

    [Fact]
    public async Task Requests_WithoutAuthToken_AreRejected()
    {
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/api/contacts");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
