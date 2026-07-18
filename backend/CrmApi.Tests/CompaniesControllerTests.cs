using System.Net;
using System.Net.Http.Json;
using CrmApi.Dtos;
using Microsoft.EntityFrameworkCore;

namespace CrmApi.Tests;

[Collection(CrmApiCollection.Name)]
public class CompaniesControllerTests(CrmApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Update_Valid_Persists()
    {
        var ws = await SeedWorkspaceAsync();
        var company = TestData.Company(ws.Workspace, "Old Name");
        await WithDb(async db => { db.Companies.Add(company); await db.SaveChangesAsync(); });

        var response = await ws.Client.PutAsJsonAsync($"/api/companies/{company.Id}", new UpdateCompanyRequest("New Name", "new.example.com", null));

        response.EnsureSuccessStatusCode();
        var dto = await response.Content.ReadFromJsonAsync<CompanyDto>();
        Assert.Equal("New Name", dto!.Name);
        Assert.Equal("new.example.com", dto.Domain);

        var persisted = await WithDb(db => db.Companies.SingleAsync(c => c.Id == company.Id));
        Assert.Equal("New Name", persisted.Name);
    }

    [Fact]
    public async Task Update_WithoutName_ReturnsBadRequest()
    {
        var ws = await SeedWorkspaceAsync();
        var company = TestData.Company(ws.Workspace);
        await WithDb(async db => { db.Companies.Add(company); await db.SaveChangesAsync(); });

        var response = await ws.Client.PutAsJsonAsync($"/api/companies/{company.Id}", new UpdateCompanyRequest("", null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_ForAnotherWorkspacesCompany_ReturnsNotFound()
    {
        var owner = await SeedWorkspaceAsync();
        var intruder = await SeedWorkspaceAsync();
        var company = TestData.Company(owner.Workspace, "Owner Co");
        await WithDb(async db => { db.Companies.Add(company); await db.SaveChangesAsync(); });

        var response = await intruder.Client.PutAsJsonAsync($"/api/companies/{company.Id}", new UpdateCompanyRequest("Hijacked", null, null));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var persisted = await WithDb(db => db.Companies.SingleAsync(c => c.Id == company.Id));
        Assert.Equal("Owner Co", persisted.Name);
    }

    [Fact]
    public async Task Get_ForAnotherWorkspacesCompany_ReturnsNotFound()
    {
        var owner = await SeedWorkspaceAsync();
        var intruder = await SeedWorkspaceAsync();
        var company = TestData.Company(owner.Workspace);
        await WithDb(async db => { db.Companies.Add(company); await db.SaveChangesAsync(); });

        var response = await intruder.Client.GetAsync($"/api/companies/{company.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
