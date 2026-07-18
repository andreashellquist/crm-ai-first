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
