using System.Net;
using System.Net.Http.Json;
using CrmApi.Dtos;
using Microsoft.EntityFrameworkCore;

namespace CrmApi.Tests;

[Collection(CrmApiCollection.Name)]
public class PipelineControllerTests(CrmApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task GetBoard_GroupsDealsUnderTheirStage()
    {
        var ws = await SeedWorkspaceAsync();
        var company = TestData.Company(ws.Workspace, "Initech");
        var deal = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne, company);
        await WithDb(async db =>
        {
            db.Companies.Add(company);
            db.Deals.Add(deal);
            await db.SaveChangesAsync();
        });

        var board = await ws.Client.GetFromJsonAsync<PipelineBoardDto>("/api/pipeline");

        Assert.NotNull(board);
        var stageOne = board!.Stages.Single(s => s.Id == ws.StageOne.Id);
        var dealCard = Assert.Single(stageOne.Deals);
        Assert.Equal(deal.Id, dealCard.Id);
        Assert.Equal("Initech", dealCard.Title);
        Assert.Empty(board.Stages.Single(s => s.Id == ws.StageTwo.Id).Deals);
    }

    [Fact]
    public async Task CreateDeal_WithNewCompanyName_UpsertsCompanyAndLandsInFirstStage()
    {
        var ws = await SeedWorkspaceAsync();
        var companyName = $"Initech {Guid.NewGuid():N}";

        var response = await ws.Client.PostAsJsonAsync("/api/deals",
            new CreateDealRequest(companyName, null, 1000_00, "USD", "pipeline", null, null));

        response.EnsureSuccessStatusCode();
        var detail = await response.Content.ReadFromJsonAsync<DealDetailDto>();
        Assert.NotNull(detail);
        Assert.Equal(companyName, detail!.Title);
        Assert.Equal(ws.StageOne.Name, detail.StageName); // lowest Order, per SeedWorkspaceAsync
        Assert.Equal(1000_00, detail.AmountCents);

        var companyCount = await WithDb(db => db.Companies.CountAsync(c => c.WorkspaceId == ws.Workspace.Id && c.Name == companyName));
        Assert.Equal(1, companyCount);
    }

    [Fact]
    public async Task CreateDeal_ReusesExistingCompanyWithSameName()
    {
        var ws = await SeedWorkspaceAsync();
        var company = TestData.Company(ws.Workspace, "Reused Co");
        await WithDb(async db => { db.Companies.Add(company); await db.SaveChangesAsync(); });

        var response = await ws.Client.PostAsJsonAsync("/api/deals",
            new CreateDealRequest("Reused Co", null, null, null, "pipeline", null, null));

        response.EnsureSuccessStatusCode();
        var companyCount = await WithDb(db => db.Companies.CountAsync(c => c.WorkspaceId == ws.Workspace.Id && c.Name == "Reused Co"));
        Assert.Equal(1, companyCount);
    }

    [Fact]
    public async Task CreateDeal_WithExplicitStageId_LandsThere()
    {
        var ws = await SeedWorkspaceAsync();

        var response = await ws.Client.PostAsJsonAsync("/api/deals",
            new CreateDealRequest($"Co {Guid.NewGuid():N}", ws.StageTwo.Id, null, null, "pipeline", null, null));

        response.EnsureSuccessStatusCode();
        var detail = await response.Content.ReadFromJsonAsync<DealDetailDto>();
        Assert.Equal(ws.StageTwo.Name, detail!.StageName);
    }

    [Fact]
    public async Task CreateDeal_WithContactIds_AssociatesThem()
    {
        var ws = await SeedWorkspaceAsync();
        var contact = TestData.Contact(ws.Workspace, "Jane");
        await WithDb(async db => { db.Contacts.Add(contact); await db.SaveChangesAsync(); });

        var response = await ws.Client.PostAsJsonAsync("/api/deals",
            new CreateDealRequest($"Co {Guid.NewGuid():N}", null, null, null, "pipeline", [contact.Id], null));

        response.EnsureSuccessStatusCode();
        var detail = await response.Content.ReadFromJsonAsync<DealDetailDto>();
        Assert.Contains("Jane", detail!.ContactNames);
    }

    [Fact]
    public async Task CreateDeal_WithUnknownContactId_ReturnsBadRequest()
    {
        var ws = await SeedWorkspaceAsync();

        var response = await ws.Client.PostAsJsonAsync("/api/deals",
            new CreateDealRequest($"Co {Guid.NewGuid():N}", null, null, null, "pipeline", [Guid.NewGuid().ToString("N")], null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateDeal_WithoutCompanyName_ReturnsBadRequest()
    {
        var ws = await SeedWorkspaceAsync();

        var response = await ws.Client.PostAsJsonAsync("/api/deals",
            new CreateDealRequest("", null, null, null, "pipeline", null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateDeal_WithInvalidForecastCategory_ReturnsBadRequest()
    {
        var ws = await SeedWorkspaceAsync();

        var response = await ws.Client.PostAsJsonAsync("/api/deals",
            new CreateDealRequest($"Co {Guid.NewGuid():N}", null, null, null, "not-a-category", null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateDeal_AppearsOnThePipelineBoard()
    {
        var ws = await SeedWorkspaceAsync();
        var companyName = $"Co {Guid.NewGuid():N}";

        var createResponse = await ws.Client.PostAsJsonAsync("/api/deals",
            new CreateDealRequest(companyName, null, null, null, "pipeline", null, null));
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<DealDetailDto>();

        var board = await ws.Client.GetFromJsonAsync<PipelineBoardDto>("/api/pipeline");
        var stageOne = board!.Stages.Single(s => s.Id == ws.StageOne.Id);
        Assert.Contains(stageOne.Deals, d => d.Id == created!.Id && d.Title == companyName);
    }

    [Fact]
    public async Task CreateDeal_LogsInitialStageChangeWithNullFromStage()
    {
        var ws = await SeedWorkspaceAsync();

        var response = await ws.Client.PostAsJsonAsync("/api/deals",
            new CreateDealRequest($"Co {Guid.NewGuid():N}", null, null, null, "pipeline", null, null));
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<DealDetailDto>();

        var change = await WithDb(db => db.DealStageChanges.SingleAsync(c => c.DealId == created!.Id));
        Assert.Null(change.FromStageId);
        Assert.Equal(ws.StageOne.Id, change.ToStageId);
    }

    [Fact]
    public async Task MoveDeal_LogsStageChangeWithFromAndToStage()
    {
        var ws = await SeedWorkspaceAsync();
        var deal = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne);
        await WithDb(async db => { db.Deals.Add(deal); await db.SaveChangesAsync(); });

        var response = await ws.Client.PostAsJsonAsync($"/api/deals/{deal.Id}/move", new MoveDealRequest(ws.StageTwo.Id));
        response.EnsureSuccessStatusCode();

        var change = await WithDb(db => db.DealStageChanges.SingleAsync(c => c.DealId == deal.Id));
        Assert.Equal(ws.StageOne.Id, change.FromStageId);
        Assert.Equal(ws.StageTwo.Id, change.ToStageId);
    }

    [Fact]
    public async Task MoveDeal_ToSameStage_DoesNotLogAStageChange()
    {
        var ws = await SeedWorkspaceAsync();
        var deal = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne);
        await WithDb(async db => { db.Deals.Add(deal); await db.SaveChangesAsync(); });

        var response = await ws.Client.PostAsJsonAsync($"/api/deals/{deal.Id}/move", new MoveDealRequest(ws.StageOne.Id));
        response.EnsureSuccessStatusCode();

        var count = await WithDb(db => db.DealStageChanges.CountAsync(c => c.DealId == deal.Id));
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task AddContact_AssociatesAnExistingContactWithAnExistingDeal()
    {
        var ws = await SeedWorkspaceAsync();
        var deal = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne);
        var contact = TestData.Contact(ws.Workspace, "Jane");
        await WithDb(async db =>
        {
            db.Deals.Add(deal);
            db.Contacts.Add(contact);
            await db.SaveChangesAsync();
        });

        var response = await ws.Client.PostAsJsonAsync($"/api/deals/{deal.Id}/contacts", new AddDealContactRequest(contact.Id));

        response.EnsureSuccessStatusCode();
        var detail = await response.Content.ReadFromJsonAsync<DealDetailDto>();
        Assert.Contains("Jane", detail!.ContactNames);
    }

    [Fact]
    public async Task AddContact_CalledTwice_DoesNotDuplicate()
    {
        var ws = await SeedWorkspaceAsync();
        var deal = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne);
        var contact = TestData.Contact(ws.Workspace, "Jane");
        await WithDb(async db =>
        {
            db.Deals.Add(deal);
            db.Contacts.Add(contact);
            await db.SaveChangesAsync();
        });

        (await ws.Client.PostAsJsonAsync($"/api/deals/{deal.Id}/contacts", new AddDealContactRequest(contact.Id))).EnsureSuccessStatusCode();
        (await ws.Client.PostAsJsonAsync($"/api/deals/{deal.Id}/contacts", new AddDealContactRequest(contact.Id))).EnsureSuccessStatusCode();

        var contactCount = await WithDb(db => db.Deals.Where(d => d.Id == deal.Id).SelectMany(d => d.Contacts).CountAsync());
        Assert.Equal(1, contactCount);
    }

    [Fact]
    public async Task AddContact_UnknownContact_ReturnsNotFound()
    {
        var ws = await SeedWorkspaceAsync();
        var deal = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne);
        await WithDb(async db => { db.Deals.Add(deal); await db.SaveChangesAsync(); });

        var response = await ws.Client.PostAsJsonAsync($"/api/deals/{deal.Id}/contacts", new AddDealContactRequest(Guid.NewGuid().ToString("N")));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RemoveContact_DisassociatesTheContact()
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

        var response = await ws.Client.DeleteAsync($"/api/deals/{deal.Id}/contacts/{contact.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var detail = await ws.Client.GetFromJsonAsync<DealDetailDto>($"/api/deals/{deal.Id}");
        Assert.DoesNotContain("Jane", detail!.ContactNames);
    }

    [Fact]
    public async Task AssignDeal_ToAWorkspaceMember_SetsAssigneeAndNotifies()
    {
        var ws = await SeedWorkspaceAsync();
        var assignee = TestData.User();
        var assigneeMember = TestData.Member(ws.Workspace, assignee, "member");
        var deal = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne);
        await WithDb(async db =>
        {
            db.Users.Add(assignee);
            db.WorkspaceMembers.Add(assigneeMember);
            db.Deals.Add(deal);
            await db.SaveChangesAsync();
        });

        var response = await ws.Client.PutAsJsonAsync($"/api/deals/{deal.Id}/assign", new AssignDealRequest(assignee.Id));

        response.EnsureSuccessStatusCode();
        var detail = await response.Content.ReadFromJsonAsync<DealDetailDto>();
        Assert.Equal(assignee.Id, detail!.AssignedToUserId);

        var assigneeClient = AuthedClient(assignee.Id, ws.Workspace.Id, "member");
        var notifications = await assigneeClient.GetFromJsonAsync<List<NotificationDto>>("/api/notifications");
        Assert.Contains(notifications!, n => n.Type == "deal_assigned" && n.EntityId == deal.Id);
    }

    [Fact]
    public async Task AssignDeal_ToNonMember_ReturnsBadRequest()
    {
        var ws = await SeedWorkspaceAsync();
        var deal = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne);
        await WithDb(async db => { db.Deals.Add(deal); await db.SaveChangesAsync(); });

        var response = await ws.Client.PutAsJsonAsync($"/api/deals/{deal.Id}/assign", new AssignDealRequest(Guid.NewGuid().ToString("N")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AssignDeal_ToNull_ClearsAssignment()
    {
        var ws = await SeedWorkspaceAsync();
        var deal = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne);
        deal.AssignedToUserId = ws.User.Id;
        await WithDb(async db => { db.Deals.Add(deal); await db.SaveChangesAsync(); });

        var response = await ws.Client.PutAsJsonAsync($"/api/deals/{deal.Id}/assign", new AssignDealRequest(null));

        response.EnsureSuccessStatusCode();
        var detail = await response.Content.ReadFromJsonAsync<DealDetailDto>();
        Assert.Null(detail!.AssignedToUserId);
    }

    [Fact]
    public async Task MoveDeal_UpdatesStage()
    {
        var ws = await SeedWorkspaceAsync();
        var deal = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne);
        await WithDb(async db => { db.Deals.Add(deal); await db.SaveChangesAsync(); });

        var response = await ws.Client.PostAsJsonAsync($"/api/deals/{deal.Id}/move", new MoveDealRequest(ws.StageTwo.Id));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var stageIdAfter = await WithDb(db => db.Deals.Where(d => d.Id == deal.Id).Select(d => d.StageId).SingleAsync());
        Assert.Equal(ws.StageTwo.Id, stageIdAfter);
    }

    [Fact]
    public async Task GetDeal_ReturnsContactsAndActivities()
    {
        var ws = await SeedWorkspaceAsync();
        var contact = TestData.Contact(ws.Workspace, "Jane");
        var deal = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne);
        deal.Contacts.Add(contact);
        var activity = TestData.Activity(ws.Workspace, deal, "call", "Intro call");
        await WithDb(async db =>
        {
            db.Contacts.Add(contact);
            db.Deals.Add(deal);
            db.Activities.Add(activity);
            await db.SaveChangesAsync();
        });

        var detail = await ws.Client.GetFromJsonAsync<DealDetailDto>($"/api/deals/{deal.Id}");

        Assert.NotNull(detail);
        Assert.Contains("Jane", detail!.ContactNames);
        Assert.Contains(detail.Activities, a => a.Body == "Intro call" && a.Type == "call");
    }

    [Fact]
    public async Task UpdateDeal_Valid_Persists()
    {
        var ws = await SeedWorkspaceAsync();
        var deal = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne, amountCents: 100_00);
        await WithDb(async db => { db.Deals.Add(deal); await db.SaveChangesAsync(); });

        var response = await ws.Client.PutAsJsonAsync($"/api/deals/{deal.Id}", new UpdateDealRequest(500_00, "EUR", "commit", null));

        response.EnsureSuccessStatusCode();
        var dto = await response.Content.ReadFromJsonAsync<DealDetailDto>();
        Assert.Equal(500_00, dto!.AmountCents);
        Assert.Equal("EUR", dto.Currency);
        Assert.Equal("commit", dto.ForecastCategory);

        var persisted = await WithDb(db => db.Deals.SingleAsync(d => d.Id == deal.Id));
        Assert.Equal(500_00, persisted.AmountCents);
    }

    [Fact]
    public async Task UpdateDeal_InvalidForecastCategory_ReturnsBadRequest()
    {
        var ws = await SeedWorkspaceAsync();
        var deal = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne);
        await WithDb(async db => { db.Deals.Add(deal); await db.SaveChangesAsync(); });

        var response = await ws.Client.PutAsJsonAsync($"/api/deals/{deal.Id}", new UpdateDealRequest(null, null, "made-up-category", null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateDeal_NegativeAmount_ReturnsBadRequest()
    {
        var ws = await SeedWorkspaceAsync();
        var deal = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne);
        await WithDb(async db => { db.Deals.Add(deal); await db.SaveChangesAsync(); });

        var response = await ws.Client.PutAsJsonAsync($"/api/deals/{deal.Id}", new UpdateDealRequest(-100, null, "pipeline", null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateDeal_ForAnotherWorkspacesDeal_ReturnsNotFound()
    {
        var owner = await SeedWorkspaceAsync();
        var intruder = await SeedWorkspaceAsync();
        var deal = TestData.Deal(owner.Workspace, owner.Pipeline, owner.StageOne, amountCents: 100_00);
        await WithDb(async db => { db.Deals.Add(deal); await db.SaveChangesAsync(); });

        var response = await intruder.Client.PutAsJsonAsync($"/api/deals/{deal.Id}", new UpdateDealRequest(999_00, null, "pipeline", null));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var persisted = await WithDb(db => db.Deals.SingleAsync(d => d.Id == deal.Id));
        Assert.Equal(100_00, persisted.AmountCents);
    }

    [Fact]
    public async Task LogActivity_WithInvalidType_ReturnsBadRequest()
    {
        var ws = await SeedWorkspaceAsync();
        var deal = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne);
        await WithDb(async db => { db.Deals.Add(deal); await db.SaveChangesAsync(); });

        var response = await ws.Client.PostAsJsonAsync($"/api/deals/{deal.Id}/activities", new LogActivityRequest("carrier-pigeon", "hi"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task LogActivity_WithEmptyBody_ReturnsBadRequest()
    {
        var ws = await SeedWorkspaceAsync();
        var deal = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne);
        await WithDb(async db => { db.Deals.Add(deal); await db.SaveChangesAsync(); });

        var response = await ws.Client.PostAsJsonAsync($"/api/deals/{deal.Id}/activities", new LogActivityRequest("note", "   "));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task LogActivity_Valid_PersistsAndAppearsOnDeal()
    {
        var ws = await SeedWorkspaceAsync();
        var deal = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne);
        await WithDb(async db => { db.Deals.Add(deal); await db.SaveChangesAsync(); });

        var response = await ws.Client.PostAsJsonAsync($"/api/deals/{deal.Id}/activities", new LogActivityRequest("email", "Sent proposal"));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var detail = await ws.Client.GetFromJsonAsync<DealDetailDto>($"/api/deals/{deal.Id}");
        Assert.Contains(detail!.Activities, a => a.Body == "Sent proposal" && a.Type == "email");
    }
}
