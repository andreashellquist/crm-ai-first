using System.Net;
using System.Net.Http.Json;
using CrmApi.Dtos;
using Microsoft.EntityFrameworkCore;

namespace CrmApi.Tests;

[Collection(CrmApiCollection.Name)]
public class ContactsControllerTests(CrmApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Create_WithNewCompanyName_UpsertsCompanyAndReturnsContact()
    {
        var ws = await SeedWorkspaceAsync();
        var companyName = $"Globex {Guid.NewGuid():N}";

        var response = await ws.Client.PostAsJsonAsync("/api/contacts", new CreateContactRequest("Jane", "Doe", "jane@example.com", companyName));

        response.EnsureSuccessStatusCode();
        var contact = await response.Content.ReadFromJsonAsync<ContactDto>();
        Assert.NotNull(contact);
        Assert.Equal("Jane", contact!.FirstName);
        Assert.Equal(companyName, contact.CompanyName);

        var companyCount = await WithDb(db => db.Companies.CountAsync(c => c.WorkspaceId == ws.Workspace.Id && c.Name == companyName));
        Assert.Equal(1, companyCount);
    }

    [Fact]
    public async Task Create_ReusesExistingCompanyWithSameName()
    {
        var ws = await SeedWorkspaceAsync();
        var company = TestData.Company(ws.Workspace, "Reused Co");
        await WithDb(async db => { db.Companies.Add(company); await db.SaveChangesAsync(); });

        var response = await ws.Client.PostAsJsonAsync("/api/contacts", new CreateContactRequest("Sam", null, null, "Reused Co"));
        response.EnsureSuccessStatusCode();

        var companyCount = await WithDb(db => db.Companies.CountAsync(c => c.WorkspaceId == ws.Workspace.Id && c.Name == "Reused Co"));
        Assert.Equal(1, companyCount);
    }

    [Fact]
    public async Task Create_WithoutFirstName_ReturnsBadRequest()
    {
        var ws = await SeedWorkspaceAsync();

        var response = await ws.Client.PostAsJsonAsync("/api/contacts", new CreateContactRequest("", null, null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task List_ReturnsNewestFirst()
    {
        var ws = await SeedWorkspaceAsync();
        var older = TestData.Contact(ws.Workspace, "Older");
        await WithDb(async db => { db.Contacts.Add(older); await db.SaveChangesAsync(); });

        var newerResponse = await ws.Client.PostAsJsonAsync("/api/contacts", new CreateContactRequest("Newer", null, null, null));
        newerResponse.EnsureSuccessStatusCode();

        var contacts = await ws.Client.GetFromJsonAsync<List<ContactDto>>("/api/contacts");

        Assert.NotNull(contacts);
        Assert.Equal("Newer", contacts![0].FirstName);
    }
}
