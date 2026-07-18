using CrmApi.Data;
using CrmApi.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CrmApi.Tests;

[Collection(CrmApiCollection.Name)]
public class ContactImportServiceTests(CrmApiFactory factory) : IntegrationTestBase(factory)
{
    private static readonly Dictionary<string, string> DefaultMapping = new()
    {
        ["email"] = "Email",
        ["firstName"] = "First",
        ["lastName"] = "Last",
        ["phone"] = "Phone",
        ["companyDomain"] = "Domain",
    };

    private ContactImportService Service() => Factory.Services.CreateScope().ServiceProvider.GetRequiredService<ContactImportService>();

    [Fact]
    public void Preview_ReturnsHeadersAndFirst10Rows()
    {
        var csv = "Email,First,Last\n" + string.Join("\n", Enumerable.Range(1, 15).Select(i => $"user{i}@example.com,First{i},Last{i}"));

        var result = Service().Preview(csv);

        Assert.Equal(["Email", "First", "Last"], result.Headers);
        Assert.Equal(15, result.TotalRows);
        Assert.Equal(10, result.PreviewRows.Count);
        Assert.Equal("user1@example.com", result.PreviewRows[0]["Email"]);
    }

    [Fact]
    public async Task Import_NewContactWithCompanyDomain_CreatesContactAndCompany()
    {
        var ws = await SeedWorkspaceAsync();
        var csv = "Email,First,Last,Phone,Domain\njane@acme.com,Jane,Doe,555-0100,acme.com";

        var result = await Service().Import(csv, DefaultMapping, ws.Workspace.Id);

        Assert.Equal(1, result.Created);
        Assert.Equal(0, result.Updated);
        Assert.Empty(result.Errors);

        var contact = await WithDb(db => db.Contacts.Include(c => c.Company)
            .SingleAsync(c => c.WorkspaceId == ws.Workspace.Id && c.Email == "jane@acme.com"));
        Assert.Equal("Jane", contact.FirstName);
        Assert.Equal("555-0100", contact.Phone);
        Assert.Equal("acme.com", contact.Company!.Domain);
    }

    [Fact]
    public async Task Import_TwoRowsSameDomain_ReuseSingleCompany()
    {
        var ws = await SeedWorkspaceAsync();
        var csv = "Email,First,Last,Phone,Domain\n" +
                   "jane@acme.com,Jane,Doe,,acme.com\n" +
                   "john@acme.com,John,Smith,,acme.com";

        await Service().Import(csv, DefaultMapping, ws.Workspace.Id);

        var companies = await WithDb(db => db.Companies.Where(c => c.WorkspaceId == ws.Workspace.Id && c.Domain == "acme.com").ToListAsync());
        Assert.Single(companies);
    }

    [Fact]
    public async Task Import_ExactEmailMatch_FillsBlanksOnlyWithoutOverwritingExistingValues()
    {
        var ws = await SeedWorkspaceAsync();
        var existing = TestData.Contact(ws.Workspace, "OriginalName");
        existing.Email = "jane@acme.com";
        existing.Phone = null;
        await WithDb(async db => { db.Contacts.Add(existing); await db.SaveChangesAsync(); });

        var csv = "Email,First,Last,Phone,Domain\njane@acme.com,ImportedName,Doe,555-0100,acme.com";
        var result = await Service().Import(csv, DefaultMapping, ws.Workspace.Id);

        Assert.Equal(0, result.Created);
        Assert.Equal(1, result.Updated);

        var persisted = await WithDb(db => db.Contacts.SingleAsync(c => c.Id == existing.Id));
        Assert.Equal("OriginalName", persisted.FirstName); // not overwritten — already had a value
        Assert.Equal("555-0100", persisted.Phone); // filled in — was blank
    }

    [Fact]
    public async Task Import_NoEmailButDomainAndNameMatch_UpdatesExistingContact()
    {
        var ws = await SeedWorkspaceAsync();
        var company = TestData.Company(ws.Workspace, "Acme Co");
        company.Domain = "acme.com";
        var existing = TestData.Contact(ws.Workspace, "Jane", company);
        existing.LastName = "Doe";
        await WithDb(async db => { db.Companies.Add(company); db.Contacts.Add(existing); await db.SaveChangesAsync(); });

        var csv = "Email,First,Last,Phone,Domain\n,Jane,Doe,555-0199,acme.com";
        var result = await Service().Import(csv, DefaultMapping, ws.Workspace.Id);

        Assert.Equal(0, result.Created);
        Assert.Equal(1, result.Updated);

        var persisted = await WithDb(db => db.Contacts.SingleAsync(c => c.Id == existing.Id));
        Assert.Equal("555-0199", persisted.Phone);
    }

    [Fact]
    public async Task Import_InvalidEmailFormat_RecordsRowError()
    {
        var ws = await SeedWorkspaceAsync();
        var csv = "Email,First,Last\nnot-an-email,Jane,Doe";

        var result = await Service().Import(csv, DefaultMapping, ws.Workspace.Id);

        Assert.Equal(0, result.Created);
        var error = Assert.Single(result.Errors);
        Assert.Equal(2, error.Row); // header is line 1
        Assert.Contains("not-an-email", error.Message);
    }

    [Fact]
    public async Task Import_RowWithNoIdentifyingData_IsSkippedNotErrored()
    {
        var ws = await SeedWorkspaceAsync();
        var csv = "Email,First,Last,Phone,Domain\n,,,555-0100,acme.com";

        var result = await Service().Import(csv, DefaultMapping, ws.Workspace.Id);

        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, result.Created);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task Import_TooManyRows_ThrowsImportFailedException()
    {
        var ws = await SeedWorkspaceAsync();
        var csv = "Email\n" + string.Join("\n", Enumerable.Range(1, 10_001).Select(i => $"user{i}@example.com"));

        await Assert.ThrowsAsync<ImportFailedException>(() => Service().Import(csv, new() { ["email"] = "Email" }, ws.Workspace.Id));
    }

    [Fact]
    public void Preview_EmptyCsv_ThrowsImportFailedException()
    {
        Assert.Throws<ImportFailedException>(() => Service().Preview(""));
    }
}
