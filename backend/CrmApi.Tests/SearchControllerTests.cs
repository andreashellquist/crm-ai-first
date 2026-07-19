using System.Net.Http.Json;
using CrmApi.Dtos;

namespace CrmApi.Tests;

[Collection(CrmApiCollection.Name)]
public class SearchControllerTests(CrmApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Search_ByContactName_ReturnsMatchCaseInsensitive()
    {
        var ws = await SeedWorkspaceAsync();
        var contact = TestData.Contact(ws.Workspace, "Zephyrine");
        await WithDb(async db => { db.Contacts.Add(contact); await db.SaveChangesAsync(); });

        var result = await ws.Client.GetFromJsonAsync<SearchResultsDto>("/api/search?q=zephyr");

        Assert.NotNull(result);
        var match = Assert.Single(result!.Contacts);
        Assert.Equal(contact.Id, match.Id);
    }

    [Fact]
    public async Task Search_ByCompanyName_ReturnsCompanyAndItsDeal()
    {
        var ws = await SeedWorkspaceAsync();
        var company = TestData.Company(ws.Workspace, "Zylotech Systems");
        var deal = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne, company);
        await WithDb(async db => { db.Companies.Add(company); db.Deals.Add(deal); await db.SaveChangesAsync(); });

        var result = await ws.Client.GetFromJsonAsync<SearchResultsDto>("/api/search?q=zylotech");

        Assert.NotNull(result);
        Assert.Contains(result!.Companies, r => r.Id == company.Id);
        Assert.Contains(result.Deals, r => r.Id == deal.Id);
    }

    [Fact]
    public async Task Search_WithTooShortQuery_ReturnsEmptyResultsWithoutError()
    {
        var ws = await SeedWorkspaceAsync();

        var result = await ws.Client.GetFromJsonAsync<SearchResultsDto>("/api/search?q=a");

        Assert.NotNull(result);
        Assert.Empty(result!.Contacts);
        Assert.Empty(result.Companies);
        Assert.Empty(result.Deals);
    }

    [Fact]
    public async Task Search_WithNoMatches_ReturnsEmptyLists()
    {
        var ws = await SeedWorkspaceAsync();

        var result = await ws.Client.GetFromJsonAsync<SearchResultsDto>("/api/search?q=nonexistentxyz");

        Assert.NotNull(result);
        Assert.Empty(result!.Contacts);
        Assert.Empty(result.Companies);
        Assert.Empty(result.Deals);
    }
}
