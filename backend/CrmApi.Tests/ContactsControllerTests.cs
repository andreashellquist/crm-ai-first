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

    [Fact]
    public async Task List_WithQuery_FiltersByNameOrEmailCaseInsensitive()
    {
        var ws = await SeedWorkspaceAsync();
        var jane = TestData.Contact(ws.Workspace, "Jane");
        jane.Email = "jane@example.com";
        var bob = TestData.Contact(ws.Workspace, "Bob");
        await WithDb(async db => { db.Contacts.AddRange(jane, bob); await db.SaveChangesAsync(); });

        var contacts = await ws.Client.GetFromJsonAsync<List<ContactDto>>("/api/contacts?q=JANE");

        Assert.NotNull(contacts);
        var contact = Assert.Single(contacts!);
        Assert.Equal("Jane", contact.FirstName);
    }

    [Fact]
    public async Task List_WithLifecycleStageFilter_ReturnsOnlyMatching()
    {
        var ws = await SeedWorkspaceAsync();
        var lead = TestData.Contact(ws.Workspace, "LeadPerson");
        var customer = TestData.Contact(ws.Workspace, "CustomerPerson");
        customer.LifecycleStage = "customer";
        await WithDb(async db => { db.Contacts.AddRange(lead, customer); await db.SaveChangesAsync(); });

        var contacts = await ws.Client.GetFromJsonAsync<List<ContactDto>>("/api/contacts?lifecycleStage=customer");

        Assert.NotNull(contacts);
        var contact = Assert.Single(contacts!);
        Assert.Equal("CustomerPerson", contact.FirstName);
    }

    [Fact]
    public async Task List_SortByName_OrdersAlphabetically()
    {
        var ws = await SeedWorkspaceAsync();
        var zed = TestData.Contact(ws.Workspace, "Zed");
        var amy = TestData.Contact(ws.Workspace, "Amy");
        await WithDb(async db => { db.Contacts.AddRange(zed, amy); await db.SaveChangesAsync(); });

        var contacts = await ws.Client.GetFromJsonAsync<List<ContactDto>>("/api/contacts?sort=name");

        Assert.NotNull(contacts);
        var names = contacts!.Select(c => c.FirstName).ToList();
        Assert.True(names.IndexOf("Amy") < names.IndexOf("Zed"));
    }

    [Fact]
    public async Task Get_ReturnsCompanyActivitiesAndDeals()
    {
        var ws = await SeedWorkspaceAsync();
        var company = TestData.Company(ws.Workspace, "Acme");
        var contact = TestData.Contact(ws.Workspace, "Jane", company);
        var deal = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne, company, amountCents: 300_00);
        deal.Contacts.Add(contact);
        var activity = TestData.Activity(ws.Workspace, deal, "call", "Intro call");
        activity.ContactId = contact.Id;
        await WithDb(async db =>
        {
            db.Companies.Add(company);
            db.Contacts.Add(contact);
            db.Deals.Add(deal);
            db.Activities.Add(activity);
            await db.SaveChangesAsync();
        });

        var response = await ws.Client.GetAsync($"/api/contacts/{contact.Id}");

        response.EnsureSuccessStatusCode();
        var detail = await response.Content.ReadFromJsonAsync<ContactDetailDto>();
        Assert.Equal("Jane", detail!.FirstName);
        Assert.Equal("Acme", detail.CompanyName);
        Assert.Contains(detail.Activities, a => a.Body == "Intro call");
        Assert.Contains(detail.Deals, d => d.AmountCents == 300_00);
    }

    [Fact]
    public async Task Get_ForAnotherWorkspacesContact_ReturnsNotFound()
    {
        var owner = await SeedWorkspaceAsync();
        var intruder = await SeedWorkspaceAsync();
        var contact = TestData.Contact(owner.Workspace, "Jane");
        await WithDb(async db => { db.Contacts.Add(contact); await db.SaveChangesAsync(); });

        var response = await intruder.Client.GetAsync($"/api/contacts/{contact.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_DoesNotWriteAnAuditLogEntry()
    {
        var ws = await SeedWorkspaceAsync();
        var contact = TestData.Contact(ws.Workspace, "Jane");
        await WithDb(async db => { db.Contacts.Add(contact); await db.SaveChangesAsync(); });

        (await ws.Client.GetAsync($"/api/contacts/{contact.Id}")).EnsureSuccessStatusCode();

        var auditLogCount = await WithDb(db => db.AuditLogs.CountAsync(l => l.TargetId == contact.Id));
        Assert.Equal(0, auditLogCount);
    }
}
