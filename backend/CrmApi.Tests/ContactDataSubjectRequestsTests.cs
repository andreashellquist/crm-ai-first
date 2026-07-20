using System.Net;
using System.Net.Http.Json;
using CrmApi.Dtos;
using Microsoft.EntityFrameworkCore;

namespace CrmApi.Tests;

// Data-subject access/erasure request fulfillment — see
// auth-security-expert's "Data-subject requests" section and
// docs/REGIONAL_COMPLIANCE.md §4.
[Collection(CrmApiCollection.Name)]
public class ContactDataSubjectRequestsTests(CrmApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Export_ReturnsPiiAndAssociatedActivitiesAndDeals()
    {
        var ws = await SeedWorkspaceAsync();
        var company = TestData.Company(ws.Workspace, "Acme");
        var contact = TestData.Contact(ws.Workspace, "Jane", company);
        contact.Email = "jane@acme.example";
        contact.Phone = "555-1234";
        var deal = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne, company, amountCents: 500_00);
        var activity = TestData.Activity(ws.Workspace, deal, "call", "Talked to Jane");
        activity.ContactId = contact.Id;
        await WithDb(async db =>
        {
            db.Companies.Add(company);
            db.Contacts.Add(contact);
            deal.Contacts.Add(contact);
            db.Deals.Add(deal);
            db.Activities.Add(activity);
            await db.SaveChangesAsync();
        });

        var response = await ws.Client.GetAsync($"/api/contacts/{contact.Id}/export");

        response.EnsureSuccessStatusCode();
        var export = await response.Content.ReadFromJsonAsync<ContactExportDto>();
        Assert.NotNull(export);
        Assert.Equal("Jane", export!.FirstName);
        Assert.Equal("jane@acme.example", export.Email);
        Assert.Equal("555-1234", export.Phone);
        Assert.Equal("Acme", export.CompanyName);
        Assert.Contains(export.Activities, a => a.Body == "Talked to Jane");
        Assert.Contains(export.Deals, d => d.AmountCents == 500_00);
    }

    [Fact]
    public async Task Export_AsMember_ReturnsForbidden()
    {
        var ws = await SeedWorkspaceAsync();
        var contact = TestData.Contact(ws.Workspace, "Jane");
        await WithDb(async db => { db.Contacts.Add(contact); await db.SaveChangesAsync(); });
        var memberClient = AuthedClient(ws.User.Id, ws.Workspace.Id, "member");

        var response = await memberClient.GetAsync($"/api/contacts/{contact.Id}/export");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Export_UnknownContact_ReturnsNotFound()
    {
        var ws = await SeedWorkspaceAsync();

        var response = await ws.Client.GetAsync($"/api/contacts/{Guid.NewGuid():N}/export");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Export_WritesAuditLogEntry()
    {
        var ws = await SeedWorkspaceAsync();
        var contact = TestData.Contact(ws.Workspace, "Jane");
        await WithDb(async db => { db.Contacts.Add(contact); await db.SaveChangesAsync(); });

        var response = await ws.Client.GetAsync($"/api/contacts/{contact.Id}/export");
        response.EnsureSuccessStatusCode();

        var logs = await ws.Client.GetFromJsonAsync<List<AuditLogDto>>("/api/audit-log");
        Assert.Contains(logs!, l => l.Action == "contact.exported" && l.TargetId == contact.Id);
    }

    [Fact]
    public async Task Erase_AnonymizesPiiAndSetsDeletedAt()
    {
        var ws = await SeedWorkspaceAsync();
        var contact = TestData.Contact(ws.Workspace, "Jane");
        contact.Email = "jane@example.com";
        contact.Phone = "555-1234";
        await WithDb(async db => { db.Contacts.Add(contact); await db.SaveChangesAsync(); });

        var response = await ws.Client.DeleteAsync($"/api/contacts/{contact.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var updated = await WithDb(db => db.Contacts.SingleAsync(c => c.Id == contact.Id));
        Assert.Null(updated.Email);
        Assert.Null(updated.Phone);
        Assert.Null(updated.LastName);
        Assert.Equal("[deleted contact]", updated.FirstName);
        Assert.NotNull(updated.DeletedAt);
    }

    [Fact]
    public async Task Erase_RemovesContactFromList()
    {
        var ws = await SeedWorkspaceAsync();
        var contact = TestData.Contact(ws.Workspace, "Jane");
        await WithDb(async db => { db.Contacts.Add(contact); await db.SaveChangesAsync(); });

        var response = await ws.Client.DeleteAsync($"/api/contacts/{contact.Id}");
        response.EnsureSuccessStatusCode();

        var contacts = await ws.Client.GetFromJsonAsync<List<ContactDto>>("/api/contacts");
        Assert.DoesNotContain(contacts!, c => c.Id == contact.Id);
    }

    [Fact]
    public async Task Erase_PropagatesRedactedNameToAssociatedDeal()
    {
        var ws = await SeedWorkspaceAsync();
        var contact = TestData.Contact(ws.Workspace, "Jane");
        var deal = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne);
        await WithDb(async db =>
        {
            db.Contacts.Add(contact);
            deal.Contacts.Add(contact);
            db.Deals.Add(deal);
            await db.SaveChangesAsync();
        });

        var eraseResponse = await ws.Client.DeleteAsync($"/api/contacts/{contact.Id}");
        eraseResponse.EnsureSuccessStatusCode();

        var dealDetail = await ws.Client.GetFromJsonAsync<DealDetailDto>($"/api/deals/{deal.Id}");
        Assert.Contains("[deleted contact]", dealDetail!.ContactNames);
    }

    [Fact]
    public async Task Erase_AsMember_ReturnsForbidden()
    {
        var ws = await SeedWorkspaceAsync();
        var contact = TestData.Contact(ws.Workspace, "Jane");
        await WithDb(async db => { db.Contacts.Add(contact); await db.SaveChangesAsync(); });
        var memberClient = AuthedClient(ws.User.Id, ws.Workspace.Id, "member");

        var response = await memberClient.DeleteAsync($"/api/contacts/{contact.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Erase_WritesAuditLogEntry()
    {
        var ws = await SeedWorkspaceAsync();
        var contact = TestData.Contact(ws.Workspace, "Jane");
        await WithDb(async db => { db.Contacts.Add(contact); await db.SaveChangesAsync(); });

        var response = await ws.Client.DeleteAsync($"/api/contacts/{contact.Id}");
        response.EnsureSuccessStatusCode();

        var logs = await ws.Client.GetFromJsonAsync<List<AuditLogDto>>("/api/audit-log");
        Assert.Contains(logs!, l => l.Action == "contact.erased" && l.TargetId == contact.Id);
    }

    [Fact]
    public async Task Erase_ThenReimportWithSameEmail_DoesNotRematchOntoErasedContact()
    {
        // The whole point of nulling Email on erasure: a later CSV import
        // matching on that same address must create a fresh contact, not
        // silently repopulate the erased one's PII.
        var ws = await SeedWorkspaceAsync();
        var contact = TestData.Contact(ws.Workspace, "Jane");
        contact.Email = "jane@example.com";
        await WithDb(async db => { db.Contacts.Add(contact); await db.SaveChangesAsync(); });

        var eraseResponse = await ws.Client.DeleteAsync($"/api/contacts/{contact.Id}");
        eraseResponse.EnsureSuccessStatusCode();

        var createResponse = await ws.Client.PostAsJsonAsync("/api/contacts",
            new CreateContactRequest("Jane", "Reimported", "jane@example.com", null));
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<ContactDto>();

        Assert.NotEqual(contact.Id, created!.Id);
    }
}
