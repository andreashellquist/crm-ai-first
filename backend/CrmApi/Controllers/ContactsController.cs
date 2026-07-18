using System.Text.Json;
using CrmApi.Data;
using CrmApi.Dtos;
using CrmApi.Models;
using CrmApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmApi.Controllers;

[ApiController]
[Route("api/contacts")]
[Authorize]
public class ContactsController(AppDbContext db, CurrentUser current) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<ContactDto>>> List()
    {
        var contacts = await db.Contacts
            .Include(c => c.Company)
            .Where(c => c.WorkspaceId == current.WorkspaceId && c.DeletedAt == null)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();
        return Ok(contacts.Select(ToDto).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<ContactDto>> Create(CreateContactRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FirstName))
            return BadRequest("First name is required");

        var fieldDefs = await db.FieldDefinitions
            .Where(f => f.WorkspaceId == current.WorkspaceId && f.EntityType == "contact")
            .ToListAsync();
        string customFields;
        try
        {
            customFields = CustomFieldValidator.ValidateAndSerialize(fieldDefs, request.CustomFields);
        }
        catch (CustomFieldValidationException ex)
        {
            return BadRequest(ex.Message);
        }

        string? companyId = null;
        if (!string.IsNullOrWhiteSpace(request.CompanyName))
        {
            var company = await db.Companies.FirstOrDefaultAsync(
                c => c.WorkspaceId == current.WorkspaceId && c.Name == request.CompanyName);
            if (company is null)
            {
                company = new Company { WorkspaceId = current.WorkspaceId, Name = request.CompanyName };
                db.Companies.Add(company);
                await db.SaveChangesAsync();
            }
            companyId = company.Id;
        }

        var contact = new Contact
        {
            WorkspaceId = current.WorkspaceId,
            FirstName = request.FirstName,
            LastName = string.IsNullOrWhiteSpace(request.LastName) ? null : request.LastName,
            Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email,
            CompanyId = companyId,
            CustomFields = customFields,
        };
        db.Contacts.Add(contact);
        await db.SaveChangesAsync();

        return Ok(new ContactDto(contact.Id, contact.FirstName, contact.LastName, contact.Email, request.CompanyName, contact.LifecycleStage,
            JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(contact.CustomFields) ?? []));
    }

    private static ContactDto ToDto(Contact c) => new(
        c.Id, c.FirstName, c.LastName, c.Email, c.Company?.Name, c.LifecycleStage,
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(c.CustomFields) ?? []);

    // Synchronous — parsing headers + a 10-row preview is fast, and the
    // frontend needs it immediately to build the column-mapping UI (no
    // job/poll round trip for this step). See the csv-import-dedupe skill.
    [HttpPost("import/preview")]
    public ActionResult<CsvImportPreviewResponse> ImportPreview(CsvImportPreviewRequest request, [FromServices] ContactImportService importService)
    {
        try
        {
            return Ok(importService.Preview(request.CsvContent));
        }
        catch (ImportFailedException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    // The actual import runs as a background job (per backend-api-engineer:
    // "anything that isn't a fast single-record write goes through the job
    // queue") — a real CSV of any size will exceed a request timeout, and the
    // UI shows progress via polling rather than blocking.
    [HttpPost("import")]
    public async Task<ActionResult<CsvImportResponse>> Import(CsvImportRequest request, [FromServices] JobQueueService queue)
    {
        var jobId = await queue.Enqueue(
            "import_contacts",
            new { csvContent = request.CsvContent, columnMapping = request.ColumnMapping, workspaceId = current.WorkspaceId },
            current.WorkspaceId);
        return Ok(new CsvImportResponse(jobId));
    }
}
