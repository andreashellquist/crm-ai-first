using CrmApi.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CrmApi.Tests;

[Collection(CrmApiCollection.Name)]
public class ReportingServiceTests(CrmApiFactory factory) : IntegrationTestBase(factory)
{
    private ReportingService Service() => Factory.Services.CreateScope().ServiceProvider.GetRequiredService<ReportingService>();

    [Fact]
    public async Task Refresh_ComputesPipelineSnapshotByStage()
    {
        var ws = await SeedWorkspaceAsync(); // StageOne probability=10, StageTwo probability=100
        var dealA = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne, amountCents: 100_00);
        var dealB = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne, amountCents: 200_00);
        var dealC = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageTwo, amountCents: 500_00);
        await WithDb(async db => { db.Deals.AddRange(dealA, dealB, dealC); await db.SaveChangesAsync(); });

        await Service().RefreshWorkspaceReports(ws.Workspace.Id);

        var stageOneSnapshot = await WithDb(db => db.PipelineSnapshots.SingleAsync(s => s.WorkspaceId == ws.Workspace.Id && s.StageId == ws.StageOne.Id));
        Assert.Equal(2, stageOneSnapshot.DealCount);
        Assert.Equal(300_00, stageOneSnapshot.DealValueCents);
        Assert.Equal(30_00, stageOneSnapshot.WeightedValueCents); // 300_00 * 10%

        var stageTwoSnapshot = await WithDb(db => db.PipelineSnapshots.SingleAsync(s => s.WorkspaceId == ws.Workspace.Id && s.StageId == ws.StageTwo.Id));
        Assert.Equal(1, stageTwoSnapshot.DealCount);
        Assert.Equal(500_00, stageTwoSnapshot.DealValueCents);
        Assert.Equal(500_00, stageTwoSnapshot.WeightedValueCents); // 100% probability
    }

    [Fact]
    public async Task Refresh_ExcludesClosedDealsFromPipelineButIncludesInForecast()
    {
        var ws = await SeedWorkspaceAsync();
        var closedDeal = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageTwo, amountCents: 400_00);
        closedDeal.ClosedAt = DateTime.UtcNow;
        closedDeal.ForecastCategory = "closed";
        await WithDb(async db => { db.Deals.Add(closedDeal); await db.SaveChangesAsync(); });

        await Service().RefreshWorkspaceReports(ws.Workspace.Id);

        var pipelineSnapshot = await WithDb(db => db.PipelineSnapshots.FirstOrDefaultAsync(s => s.WorkspaceId == ws.Workspace.Id && s.StageId == ws.StageTwo.Id));
        Assert.Null(pipelineSnapshot); // no open deals in this stage — no row

        var forecastSnapshot = await WithDb(db => db.ForecastSnapshots.SingleAsync(s => s.WorkspaceId == ws.Workspace.Id && s.ForecastCategory == "closed"));
        Assert.Equal(1, forecastSnapshot.DealCount);
        Assert.Equal(400_00, forecastSnapshot.DealValueCents);
    }

    [Fact]
    public async Task Refresh_ComputesForecastSnapshotByCategory()
    {
        var ws = await SeedWorkspaceAsync();
        var commitDeal = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne, amountCents: 150_00);
        commitDeal.ForecastCategory = "commit";
        var pipelineDeal = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne, amountCents: 250_00);
        await WithDb(async db => { db.Deals.AddRange(commitDeal, pipelineDeal); await db.SaveChangesAsync(); });

        await Service().RefreshWorkspaceReports(ws.Workspace.Id);

        var commitSnapshot = await WithDb(db => db.ForecastSnapshots.SingleAsync(s => s.WorkspaceId == ws.Workspace.Id && s.ForecastCategory == "commit"));
        Assert.Equal(1, commitSnapshot.DealCount);
        Assert.Equal(150_00, commitSnapshot.DealValueCents);

        var pipelineCategorySnapshot = await WithDb(db => db.ForecastSnapshots.SingleAsync(s => s.WorkspaceId == ws.Workspace.Id && s.ForecastCategory == "pipeline"));
        Assert.Equal(1, pipelineCategorySnapshot.DealCount);
    }

    [Fact]
    public async Task Refresh_ActivityMetrics_OnlyCountsWithinThirtyDayWindow()
    {
        var ws = await SeedWorkspaceAsync();
        var deal = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne);
        var recent = TestData.Activity(ws.Workspace, deal, "call");
        var old = TestData.Activity(ws.Workspace, deal, "call");
        old.CreatedAt = DateTime.UtcNow.AddDays(-40);
        await WithDb(async db => { db.Deals.Add(deal); db.Activities.AddRange(recent, old); await db.SaveChangesAsync(); });

        await Service().RefreshWorkspaceReports(ws.Workspace.Id);

        var metrics = await WithDb(db => db.ActivityMetrics.Where(m => m.WorkspaceId == ws.Workspace.Id).ToListAsync());
        var totalCount = metrics.Sum(m => m.Count);
        Assert.Equal(1, totalCount); // only the recent activity, not the 40-day-old one
    }

    [Fact]
    public async Task Refresh_IsIdempotent_RunningTwiceProducesNoDuplicates()
    {
        var ws = await SeedWorkspaceAsync();
        var deal = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne, amountCents: 100_00);
        await WithDb(async db => { db.Deals.Add(deal); await db.SaveChangesAsync(); });

        await Service().RefreshWorkspaceReports(ws.Workspace.Id);
        await Service().RefreshWorkspaceReports(ws.Workspace.Id);

        var count = await WithDb(db => db.PipelineSnapshots.CountAsync(s => s.WorkspaceId == ws.Workspace.Id && s.StageId == ws.StageOne.Id));
        Assert.Equal(1, count);
    }
}
