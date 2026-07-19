using System.Net;
using System.Net.Http.Json;
using CrmApi.Dtos;
using Microsoft.EntityFrameworkCore;

namespace CrmApi.Tests;

[Collection(CrmApiCollection.Name)]
public class ListingsControllerTests(CrmApiFactory factory) : IntegrationTestBase(factory)
{
    private async Task EnableListingsModuleAsync(WorkspaceScenario ws)
    {
        var response = await ws.Client.PutAsJsonAsync("/api/workspace/settings",
            new UpdateWorkspaceSettingsRequest(null, ["listings"]));
        response.EnsureSuccessStatusCode();
    }

    private async Task<string> SeedDealAsync(WorkspaceScenario ws)
    {
        var deal = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne);
        await WithDb(async db =>
        {
            db.Deals.Add(deal);
            await db.SaveChangesAsync();
        });
        return deal.Id;
    }

    [Fact]
    public async Task Get_ModuleNotEnabled_ReturnsForbidden()
    {
        var ws = await SeedWorkspaceAsync();
        var dealId = await SeedDealAsync(ws);

        var response = await ws.Client.GetAsync($"/api/deals/{dealId}/listing");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_ModuleEnabled_NoListingYet_ReturnsEmptyDefaults()
    {
        var ws = await SeedWorkspaceAsync();
        await EnableListingsModuleAsync(ws);
        var dealId = await SeedDealAsync(ws);

        var listing = await ws.Client.GetFromJsonAsync<ListingDto>($"/api/deals/{dealId}/listing");

        Assert.NotNull(listing);
        Assert.Equal(dealId, listing!.DealId);
        Assert.Null(listing.ListingAgentName);
    }

    [Fact]
    public async Task Upsert_ModuleEnabled_CreatesThenUpdatesListing()
    {
        var ws = await SeedWorkspaceAsync();
        await EnableListingsModuleAsync(ws);
        var dealId = await SeedDealAsync(ws);

        var create = await ws.Client.PutAsJsonAsync($"/api/deals/{dealId}/listing",
            new UpsertListingRequest("Jordan Realtor", "https://example.com/listing/1", null, 2.5m));
        create.EnsureSuccessStatusCode();
        var created = await create.Content.ReadFromJsonAsync<ListingDto>();
        Assert.Equal("Jordan Realtor", created!.ListingAgentName);

        var update = await ws.Client.PutAsJsonAsync($"/api/deals/{dealId}/listing",
            new UpsertListingRequest("Alex Realtor", "https://example.com/listing/1", null, 3m));
        update.EnsureSuccessStatusCode();
        var updated = await update.Content.ReadFromJsonAsync<ListingDto>();
        Assert.Equal("Alex Realtor", updated!.ListingAgentName);

        // Still one row, not a duplicate, per the unique index on DealId.
        var count = await WithDb(db => db.Listings.CountAsync(l => l.DealId == dealId));
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Upsert_ModuleNotEnabled_ReturnsForbidden()
    {
        var ws = await SeedWorkspaceAsync();
        var dealId = await SeedDealAsync(ws);

        var response = await ws.Client.PutAsJsonAsync($"/api/deals/{dealId}/listing",
            new UpsertListingRequest("Someone", null, null, null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Upsert_CommissionPercentOutOfRange_ReturnsBadRequest()
    {
        var ws = await SeedWorkspaceAsync();
        await EnableListingsModuleAsync(ws);
        var dealId = await SeedDealAsync(ws);

        var response = await ws.Client.PutAsJsonAsync($"/api/deals/{dealId}/listing",
            new UpsertListingRequest(null, null, null, 150m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Listing_IsIsolatedPerWorkspace()
    {
        var ws1 = await SeedWorkspaceAsync();
        var ws2 = await SeedWorkspaceAsync();
        await EnableListingsModuleAsync(ws1);
        await EnableListingsModuleAsync(ws2);
        var ws1DealId = await SeedDealAsync(ws1);

        var upsert = await ws1.Client.PutAsJsonAsync($"/api/deals/{ws1DealId}/listing",
            new UpsertListingRequest("Workspace 1 Agent", null, null, null));
        upsert.EnsureSuccessStatusCode();

        // ws2 has the module enabled too, but ws1's deal doesn't exist in
        // ws2's workspace scope — a teammate at a different workspace can't
        // read or overwrite it just by guessing the deal id.
        var response = await ws2.Client.GetAsync($"/api/deals/{ws1DealId}/listing");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
