using System.Text.RegularExpressions;
using CrmApi.Data;
using CrmApi.Dtos;
using CrmApi.Models;
using Microsoft.EntityFrameworkCore;

namespace CrmApi.Services;

public class ImportFailedException(string message) : Exception(message);

// CSV contact import with dedup — see the csv-import-dedupe skill.
// Deliberately v1-scoped to the fixed Contact fields below; custom-field
// mapping is a follow-up (CustomFieldValidator is entity-type-agnostic, so
// wiring it here later is additive, not a rework).
public class ContactImportService(AppDbContext db)
{
    // Guards against a pathological upload turning an in-process background
    // job into a multi-minute/OOM operation — generous for this app's
    // SMB-scale target (docs/PRODUCT_SCOPE.md: 5-500 seats per workspace),
    // revisit if a customer's real export exceeds it.
    private const int MaxRows = 10_000;

    private static readonly string[] ImportableFields = ["email", "firstName", "lastName", "phone", "companyName", "companyDomain"];
    private static readonly Regex EmailPattern = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

    public CsvImportPreviewResponse Preview(string csvContent)
    {
        var rows = CsvParser.Parse(csvContent);
        if (rows.Count == 0) throw new ImportFailedException("CSV file is empty");

        var headers = rows[0];
        var dataRows = rows.Skip(1).ToList();
        var previewRows = dataRows.Take(10)
            .Select(row => headers.Select((h, i) => (h, value: i < row.Length ? row[i] : "")).ToDictionary(x => x.h, x => x.value))
            .ToList();

        return new CsvImportPreviewResponse(headers.ToList(), previewRows, dataRows.Count);
    }

    public async Task<ContactImportResult> Import(string csvContent, Dictionary<string, string> columnMapping, string workspaceId)
    {
        var rows = CsvParser.Parse(csvContent);
        if (rows.Count == 0) throw new ImportFailedException("CSV file is empty");

        var headers = rows[0];
        var dataRows = rows.Skip(1).ToList();
        if (dataRows.Count > MaxRows) throw new ImportFailedException($"CSV has {dataRows.Count} rows — the import limit is {MaxRows}");

        // Resolve each mapped field to a column index once, up front, rather
        // than re-searching headers per row.
        var fieldIndex = new Dictionary<string, int>();
        foreach (var field in ImportableFields)
        {
            if (columnMapping.TryGetValue(field, out var header))
            {
                var index = headers.IndexOf(header);
                if (index >= 0) fieldIndex[field] = index;
            }
        }

        // Load the whole workspace's existing Contacts/Companies into memory
        // up front and match/create against these dictionaries rather than
        // querying per row — an added-but-unsaved entity isn't visible to a
        // fresh LINQ query, and per-row round trips don't scale. A v1
        // tradeoff for this app's SMB-scale target (see MaxRows above); a
        // workspace with an unusually large existing contact base would want
        // this changed to indexed per-row lookups.
        var existingContacts = await db.Contacts
            .Where(c => c.WorkspaceId == workspaceId && c.DeletedAt == null)
            .ToListAsync();
        var existingCompanies = await db.Companies
            .Where(c => c.WorkspaceId == workspaceId && c.DeletedAt == null)
            .ToListAsync();

        var contactsByEmail = existingContacts
            .Where(c => !string.IsNullOrWhiteSpace(c.Email))
            .GroupBy(c => c.Email!.Trim().ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.First());
        var companiesByDomain = existingCompanies
            .Where(c => !string.IsNullOrWhiteSpace(c.Domain))
            .GroupBy(c => NormalizeDomain(c.Domain))
            .ToDictionary(g => g.Key, g => g.First());
        var companiesByName = existingCompanies
            .GroupBy(c => c.Name.Trim().ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.First());

        var created = 0;
        var updated = 0;
        var skipped = 0;
        var errors = new List<ImportRowError>();

        for (var i = 0; i < dataRows.Count; i++)
        {
            var line = i + 2; // header is line 1
            var row = dataRows[i];

            string? Field(string name)
            {
                if (!fieldIndex.TryGetValue(name, out var idx) || idx >= row.Length) return null;
                var value = row[idx].Trim();
                return value.Length > 0 ? value : null;
            }

            var email = Field("email");
            var firstName = Field("firstName");
            var lastName = Field("lastName");
            var phone = Field("phone");
            var companyName = Field("companyName");
            var companyDomain = Field("companyDomain");

            if (email is null && firstName is null && lastName is null)
            {
                skipped++;
                continue;
            }
            if (email is not null && !EmailPattern.IsMatch(email))
            {
                errors.Add(new ImportRowError(line, $"Invalid email format: \"{email}\""));
                continue;
            }

            var company = ResolveCompany(companyName, companyDomain, email, workspaceId, companiesByDomain, companiesByName);

            Contact? match = null;
            if (email is not null) contactsByEmail.TryGetValue(email.ToLowerInvariant(), out match);
            if (match is null && company is not null && (firstName is not null || lastName is not null))
            {
                match = existingContacts.FirstOrDefault(c =>
                    c.CompanyId == company.Id &&
                    string.Equals(c.FirstName?.Trim(), firstName, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(c.LastName?.Trim(), lastName, StringComparison.OrdinalIgnoreCase));
            }

            if (match is not null)
            {
                ApplyFillBlanksOnly(match, firstName, lastName, email, phone, company?.Id);
                updated++;
            }
            else
            {
                var contact = new Contact
                {
                    WorkspaceId = workspaceId,
                    FirstName = firstName,
                    LastName = lastName,
                    Email = email,
                    Phone = phone,
                    CompanyId = company?.Id,
                };
                db.Contacts.Add(contact);
                existingContacts.Add(contact);
                if (email is not null) contactsByEmail[email.ToLowerInvariant()] = contact;
                created++;
            }
        }

        await db.SaveChangesAsync();
        return new ContactImportResult(dataRows.Count, created, updated, skipped, errors);
    }

    private Company? ResolveCompany(
        string? companyName,
        string? companyDomain,
        string? email,
        string workspaceId,
        Dictionary<string, Company> companiesByDomain,
        Dictionary<string, Company> companiesByName)
    {
        var domain = companyDomain is not null ? NormalizeDomain(companyDomain)
            : email is not null ? NormalizeDomain(email.Split('@').Last())
            : null;

        if (domain is not null && companiesByDomain.TryGetValue(domain, out var byDomain)) return byDomain;
        if (domain is null && companyName is not null && companiesByName.TryGetValue(companyName.Trim().ToLowerInvariant(), out var byName)) return byName;

        if (companyName is null && domain is null) return null;

        var company = new Company
        {
            WorkspaceId = workspaceId,
            Name = companyName ?? domain!,
            Domain = domain,
        };
        db.Companies.Add(company);
        if (domain is not null) companiesByDomain[domain] = company;
        if (companyName is not null) companiesByName[companyName.Trim().ToLowerInvariant()] = company;
        return company;
    }

    private static void ApplyFillBlanksOnly(Contact contact, string? firstName, string? lastName, string? email, string? phone, string? companyId)
    {
        var changed = false;
        if (string.IsNullOrWhiteSpace(contact.FirstName) && firstName is not null) { contact.FirstName = firstName; changed = true; }
        if (string.IsNullOrWhiteSpace(contact.LastName) && lastName is not null) { contact.LastName = lastName; changed = true; }
        if (string.IsNullOrWhiteSpace(contact.Email) && email is not null) { contact.Email = email; changed = true; }
        if (string.IsNullOrWhiteSpace(contact.Phone) && phone is not null) { contact.Phone = phone; changed = true; }
        if (contact.CompanyId is null && companyId is not null) { contact.CompanyId = companyId; changed = true; }
        if (changed) contact.UpdatedAt = DateTime.UtcNow;
    }

    private static string NormalizeDomain(string? domain)
    {
        var d = (domain ?? "").Trim().ToLowerInvariant();
        return d.StartsWith("www.") ? d[4..] : d;
    }
}
