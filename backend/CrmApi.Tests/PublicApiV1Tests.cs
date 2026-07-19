using System.Net;
using System.Net.Http.Json;
using CrmApi.Dtos;
using CrmApi.Models;
using Microsoft.EntityFrameworkCore;

namespace CrmApi.Tests;

[Collection(CrmApiCollection.Name)]
public class PublicApiV1Tests(CrmApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Contacts_MissingApiKeyHeader_ReturnsUnauthorized()
    {
        var client = Factory.CreateClient();
        var response = await client.GetAsync("/api/v1/contacts");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Contacts_InvalidApiKey_ReturnsUnauthorized()
    {
        var client = ApiKeyClient("crm_live_not-a-real-key");
        var response = await client.GetAsync("/api/v1/contacts");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Contacts_KeyMissingScope_ReturnsForbidden()
    {
        var ws = await SeedWorkspaceAsync();
        var rawKey = await CreateApiKeyAsync(ws.Workspace.Id, ws.User.Id, "deals:read"); // no contacts:read

        var client = ApiKeyClient(rawKey);
        var response = await client.GetAsync("/api/v1/contacts");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Contacts_ValidScopedKey_ReturnsWorkspaceContacts()
    {
        var ws = await SeedWorkspaceAsync();
        var contact = TestData.Contact(ws.Workspace, "Jane");
        await WithDb(async db => { db.Contacts.Add(contact); await db.SaveChangesAsync(); });
        var rawKey = await CreateApiKeyAsync(ws.Workspace.Id, ws.User.Id, "contacts:read");

        var client = ApiKeyClient(rawKey);
        var contacts = await client.GetFromJsonAsync<List<PublicContactDto>>("/api/v1/contacts");

        Assert.NotNull(contacts);
        Assert.Single(contacts!);
        Assert.Equal("Jane", contacts![0].FirstName);
    }

    [Fact]
    public async Task Contacts_NeverReturnAnotherWorkspacesData()
    {
        var owner = await SeedWorkspaceAsync();
        var intruder = await SeedWorkspaceAsync();
        var ownerContact = TestData.Contact(owner.Workspace, "Owner's Contact");
        await WithDb(async db => { db.Contacts.Add(ownerContact); await db.SaveChangesAsync(); });

        var intruderKey = await CreateApiKeyAsync(intruder.Workspace.Id, intruder.User.Id, "contacts:read");
        var client = ApiKeyClient(intruderKey);

        var contacts = await client.GetFromJsonAsync<List<PublicContactDto>>("/api/v1/contacts");
        Assert.Empty(contacts!);

        var getById = await client.GetAsync($"/api/v1/contacts/{ownerContact.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getById.StatusCode);
    }

    [Fact]
    public async Task Deals_ValidScopedKey_ReturnsContactIdsAndCanonicalFields()
    {
        var ws = await SeedWorkspaceAsync();
        var company = TestData.Company(ws.Workspace);
        var contact = TestData.Contact(ws.Workspace, "Jane", company);
        var deal = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne, company, amountCents: 50_00);
        deal.Contacts = [contact];
        await WithDb(async db =>
        {
            db.Companies.Add(company);
            db.Contacts.Add(contact);
            db.Deals.Add(deal);
            await db.SaveChangesAsync();
        });
        var rawKey = await CreateApiKeyAsync(ws.Workspace.Id, ws.User.Id, "deals:read");

        var client = ApiKeyClient(rawKey);
        var deals = await client.GetFromJsonAsync<List<PublicDealDto>>("/api/v1/deals");

        Assert.NotNull(deals);
        var dto = Assert.Single(deals!);
        Assert.Equal(company.Id, dto.CompanyId);
        Assert.Equal(ws.StageOne.Id, dto.StageId);
        Assert.Contains(contact.Id, dto.ContactIds);
    }

    [Fact]
    public async Task Companies_ValidScopedKey_ReturnsWorkspaceCompanies()
    {
        var ws = await SeedWorkspaceAsync();
        var company = TestData.Company(ws.Workspace, "Acme");
        await WithDb(async db => { db.Companies.Add(company); await db.SaveChangesAsync(); });
        var rawKey = await CreateApiKeyAsync(ws.Workspace.Id, ws.User.Id, "companies:read");

        var client = ApiKeyClient(rawKey);
        var companies = await client.GetFromJsonAsync<List<PublicCompanyDto>>("/api/v1/companies");

        Assert.NotNull(companies);
        Assert.Contains(companies!, c => c.Name == "Acme");
    }

    [Fact]
    public async Task FieldDefinitions_ValidScopedKey_ReturnsWorkspaceFieldDefinitions()
    {
        var ws = await SeedWorkspaceAsync();
        await WithDb(async db =>
        {
            db.FieldDefinitions.Add(new FieldDefinition
            {
                WorkspaceId = ws.Workspace.Id,
                EntityType = "deal",
                Key = "bedrooms",
                Label = "Bedrooms",
                FieldType = "number",
            });
            await db.SaveChangesAsync();
        });
        var rawKey = await CreateApiKeyAsync(ws.Workspace.Id, ws.User.Id, "field-definitions:read");

        var client = ApiKeyClient(rawKey);
        var defs = await client.GetFromJsonAsync<List<FieldDefinitionDto>>("/api/v1/field-definitions?entityType=deal");

        Assert.NotNull(defs);
        Assert.Contains(defs!, d => d.Key == "bedrooms");
    }
}
