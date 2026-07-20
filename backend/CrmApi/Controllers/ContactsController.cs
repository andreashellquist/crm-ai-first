using System.Text.Json;
using CrmApi.Authorization;
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
public class ContactsController(AppDbContext db, CurrentUser current, AuditLogService audit) : ControllerBase
{
    // q/lifecycleStage/sort are shareable/bookmarkable via URL search params
    // (frontend-engineer's convention for record-table filters) — the same
    // query string is also what a SavedView persists (SavedView.QueryString).
    [HttpGet]
    public async Task<ActionResult<List<ContactDto>>> List(
        [FromQuery] string? q, [FromQuery] string? lifecycleStage, [FromQuery] string? sort)
    {
        var query = db.Contacts
            .Include(c => c.Company)
            .Where(c => c.WorkspaceId == current.WorkspaceId && c.DeletedAt == null);

        if (!string.IsNullOrWhiteSpace(q) && q.Trim().Length >= 2)
        {
            var pattern = $"%{q.Trim()}%";
            query = query.Where(c => EF.Functions.ILike(c.FirstName ?? "", pattern)
                || EF.Functions.ILike(c.LastName ?? "", pattern)
                || EF.Functions.ILike(c.Email ?? "", pattern));
        }
        if (!string.IsNullOrWhiteSpace(lifecycleStage))
        {
            query = query.Where(c => c.LifecycleStage == lifecycleStage);
        }

        query = sort switch
        {
            "name" => query.OrderBy(c => c.FirstName).ThenBy(c => c.LastName),
            "-name" => query.OrderByDescending(c => c.FirstName).ThenByDescending(c => c.LastName),
            "createdAt" => query.OrderBy(c => c.CreatedAt),
            _ => query.OrderByDescending(c => c.CreatedAt), // "-createdAt" and the default
        };

        var contacts = await query.ToListAsync();
        return Ok(contacts.Select(ToDto).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<ContactDto>> Create(CreateContactRequest request, [FromServices] WebhookDeliveryService webhooks)
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
        await webhooks.Enqueue(current.WorkspaceId, "contact.created", new { contactId = contact.Id });

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

    // Data-subject access request fulfillment (GDPR/CCPA "what do you have
    // on me") — see auth-security-expert's "Data-subject requests" section
    // and docs/REGIONAL_COMPLIANCE.md §4, which flagged this as a real gap:
    // schema-only DeletedAt columns existed with no way to act on them.
    // Owner/admin only, same sensitivity tier as SSO/workspace-settings
    // config — this fulfills a real external request, not routine CRUD.
    [HttpGet("{id}/export")]
    [RequireRole("owner", "admin")]
    public async Task<ActionResult<ContactExportDto>> Export(string id)
    {
        var contact = await db.Contacts
            .Include(c => c.Company)
            .Include(c => c.Activities.OrderByDescending(a => a.CreatedAt))
            .Include(c => c.Deals).ThenInclude(d => d.Company)
            .FirstOrDefaultAsync(c => c.Id == id && c.WorkspaceId == current.WorkspaceId);
        if (contact is null) return NotFound();

        audit.Log(current.WorkspaceId, current.UserId, AuditLogService.Actions.ContactExported, "Contact", contact.Id);
        await db.SaveChangesAsync();

        return Ok(new ContactExportDto(
            contact.Id,
            contact.FirstName,
            contact.LastName,
            contact.Email,
            contact.Phone,
            contact.LifecycleStage,
            contact.Company?.Name,
            JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(contact.CustomFields) ?? [],
            contact.Activities.Select(a => new ContactExportActivityDto(a.Id, a.Type, a.Body, a.CreatedAt)).ToList(),
            contact.Deals.Select(d => new ContactExportDealDto(d.Id, d.Company?.Name, d.AmountCents, d.Currency, d.CreatedAt)).ToList(),
            DateTime.UtcNow
        ));
    }

    // Erasure — anonymizes this contact's own PII fields in place rather
    // than hard-deleting the row: Deals/Activities that reference this
    // contact are this workspace's own business records, not the data
    // subject's personal data, and must survive the request intact (a won
    // deal shouldn't vanish because a participant asked to be forgotten).
    // Real, deliberate scope limit (matches auth-security-expert's own
    // framing: "retrofitting deletion across an AI-features codebase full
    // of caches, embeddings, and summaries is much harder than building it
    // in from the start"): this does NOT re-run or scrub Deal.AiSummary,
    // which may have been generated from Activity text mentioning this
    // contact by name — regenerating cached AI summaries on erasure is a
    // real follow-up, not covered by this pass.
    [HttpDelete("{id}")]
    [RequireRole("owner", "admin")]
    public async Task<IActionResult> Erase(string id)
    {
        var contact = await db.Contacts.FirstOrDefaultAsync(c => c.Id == id && c.WorkspaceId == current.WorkspaceId);
        if (contact is null) return NotFound();

        contact.FirstName = "[deleted contact]";
        contact.LastName = null;
        contact.Email = null;
        contact.Phone = null;
        contact.CustomFields = "{}"; // may hold PII in workspace-defined fields we can't selectively distinguish
        contact.DeletedAt ??= DateTime.UtcNow;
        contact.UpdatedAt = DateTime.UtcNow;

        audit.Log(current.WorkspaceId, current.UserId, AuditLogService.Actions.ContactErased, "Contact", contact.Id);
        await db.SaveChangesAsync();
        return NoContent();
    }
}
