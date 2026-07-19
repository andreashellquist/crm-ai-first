using System.Net.Http.Json;
using CrmApi.Dtos;

namespace CrmApi.Tests;

[Collection(CrmApiCollection.Name)]
public class ReportsControllerTests(CrmApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Get_ReturnsAllStagesEvenWithoutOpenDeals()
    {
        var ws = await SeedWorkspaceAsync();

        var report = await ws.Client.GetFromJsonAsync<ReportsResponse>("/api/reports");

        Assert.NotNull(report);
        Assert.Equal(2, report!.Pipeline.Count); // StageOne + StageTwo, both zeroed
        Assert.All(report.Pipeline, r => Assert.Equal(0, r.DealCount));
    }

    [Fact]
    public async Task Refresh_ThenGet_ReflectsMovedDeal()
    {
        var ws = await SeedWorkspaceAsync();
        var deal = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne, amountCents: 300_00);
        await WithDb(async db => { db.Deals.Add(deal); await db.SaveChangesAsync(); });

        var moveResponse = await ws.Client.PostAsJsonAsync($"/api/deals/{deal.Id}/move", new MoveDealRequest(ws.StageTwo.Id));
        moveResponse.EnsureSuccessStatusCode();
        await ProcessAllPendingJobsAsync(); // drains the refresh_reports job MoveDeal enqueued

        var report = await ws.Client.GetFromJsonAsync<ReportsResponse>("/api/reports");

        Assert.NotNull(report);
        var stageTwoRow = report!.Pipeline.Single(r => r.StageId == ws.StageTwo.Id);
        Assert.Equal(1, stageTwoRow.DealCount);
        Assert.Equal(300_00, stageTwoRow.DealValueCents);
        var stageOneRow = report.Pipeline.Single(r => r.StageId == ws.StageOne.Id);
        Assert.Equal(0, stageOneRow.DealCount);
    }

    [Fact]
    public async Task Export_Pipeline_ReturnsCsvWithHeaderRow()
    {
        var ws = await SeedWorkspaceAsync();

        var response = await ws.Client.GetAsync("/api/reports/export?type=pipeline");

        response.EnsureSuccessStatusCode();
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        var csv = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("Stage,Deal count,Deal value (cents),Weighted value (cents)", csv);
    }

    [Fact]
    public async Task Export_WithInvalidType_ReturnsBadRequest()
    {
        var ws = await SeedWorkspaceAsync();

        var response = await ws.Client.GetAsync("/api/reports/export?type=not-a-real-type");

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }
}
