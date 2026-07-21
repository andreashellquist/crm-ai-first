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
[Route("api")]
[Authorize]
public class PipelineController(AppDbContext db, CurrentUser current) : ControllerBase
{
    private static readonly string[] ValidActivityTypes = ["call", "email", "meeting", "note"];
    private static readonly string[] ValidForecastCategories = ["pipeline", "best_case", "commit", "closed"];

    [HttpGet("pipeline")]
    public async Task<ActionResult<PipelineBoardDto>> GetBoard()
    {
        var pipeline = await db.Pipelines
            .Include(p => p.Stages.OrderBy(s => s.Order))
                .ThenInclude(s => s.Deals.Where(d => d.DeletedAt == null))
                    .ThenInclude(d => d.Company)
            .Where(p => p.WorkspaceId == current.WorkspaceId && p.IsDefault)
            .FirstOrDefaultAsync();
        if (pipeline is null) return NotFound();

        var defaultCurrency = await db.Workspaces
            .Where(w => w.Id == current.WorkspaceId)
            .Select(w => w.DefaultCurrency)
            .FirstAsync();

        var dto = new PipelineBoardDto(
            pipeline.Id,
            pipeline.Name,
            pipeline.Stages.OrderBy(s => s.Order).Select(s => new StageDto(
                s.Id,
                s.Name,
                s.Deals.Select(d => new DealCardDto(
                    d.Id,
                    d.Company?.Name ?? "Untitled deal",
                    d.AmountCents,
                    d.Currency,
                    d.AiScore,
                    d.AiScoreRationale
                )).ToList()
            )).ToList(),
            defaultCurrency
        );
        return Ok(dto);
    }

    // No other path in this app creates a Deal — Seed.cs is the only other
    // writer. Without this, a self-serve-provisioned workspace (WorkspaceProvisioningService)
    // gets a Pipeline with Stages but can never put a single Deal on it.
    [HttpPost("deals")]
    public async Task<ActionResult<DealDetailDto>> CreateDeal(
        CreateDealRequest request, [FromServices] JobQueueService queue, [FromServices] WebhookDeliveryService webhooks)
    {
        if (string.IsNullOrWhiteSpace(request.CompanyName)) return BadRequest("Company name is required");
        if (!ValidForecastCategories.Contains(request.ForecastCategory)) return BadRequest("Invalid forecast category");
        if (request.AmountCents is < 0) return BadRequest("Amount cannot be negative");

        var pipeline = await db.Pipelines
            .Include(p => p.Stages.OrderBy(s => s.Order))
            .Where(p => p.WorkspaceId == current.WorkspaceId && p.IsDefault)
            .FirstOrDefaultAsync();
        if (pipeline is null) return NotFound("No pipeline configured for this workspace");

        Stage stage;
        if (string.IsNullOrWhiteSpace(request.StageId))
        {
            stage = pipeline.Stages.OrderBy(s => s.Order).First();
        }
        else
        {
            var matchedStage = pipeline.Stages.FirstOrDefault(s => s.Id == request.StageId);
            if (matchedStage is null) return NotFound("Stage not found in this workspace's pipeline");
            stage = matchedStage;
        }

        var fieldDefs = await db.FieldDefinitions
            .Where(f => f.WorkspaceId == current.WorkspaceId && f.EntityType == "deal")
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

        // Find-or-create by name, same pattern as ContactsController.Create —
        // a Deal always belongs to a Company (DealCardDto/DealDetailDto's
        // "Title" is the company's name; this app has no free-text deal title).
        var company = await db.Companies.FirstOrDefaultAsync(c => c.WorkspaceId == current.WorkspaceId && c.Name == request.CompanyName);
        if (company is null)
        {
            company = new Company { WorkspaceId = current.WorkspaceId, Name = request.CompanyName };
            db.Companies.Add(company);
        }

        var contacts = new List<Contact>();
        if (request.ContactIds is { Count: > 0 })
        {
            contacts = await db.Contacts
                .Where(c => request.ContactIds.Contains(c.Id) && c.WorkspaceId == current.WorkspaceId && c.DeletedAt == null)
                .ToListAsync();
            if (contacts.Count != request.ContactIds.Distinct().Count())
                return BadRequest("One or more contacts were not found in this workspace");
        }

        var deal = new Deal
        {
            WorkspaceId = current.WorkspaceId,
            PipelineId = pipeline.Id,
            StageId = stage.Id,
            Company = company,
            AmountCents = request.AmountCents,
            Currency = request.Currency,
            ForecastCategory = request.ForecastCategory,
            CustomFields = customFields,
            Contacts = contacts,
        };
        db.Deals.Add(deal);
        await db.SaveChangesAsync();
        await queue.Enqueue("refresh_reports", new { workspaceId = current.WorkspaceId }, current.WorkspaceId);
        await webhooks.Enqueue(current.WorkspaceId, "deal.created", new { dealId = deal.Id, stageId = stage.Id });

        deal.Stage = stage;
        return Ok(ToDealDetailDto(deal));
    }

    [HttpPost("deals/{dealId}/move")]
    public async Task<IActionResult> MoveDeal(
        string dealId, MoveDealRequest request, [FromServices] JobQueueService queue, [FromServices] WebhookDeliveryService webhooks)
    {
        // Re-validate both IDs belong to this workspace before writing — never
        // trust a client-supplied ID is already scoped correctly.
        var deal = await db.Deals.FirstOrDefaultAsync(d => d.Id == dealId && d.WorkspaceId == current.WorkspaceId);
        var stage = await db.Stages.Include(s => s.Pipeline)
            .FirstOrDefaultAsync(s => s.Id == request.StageId && s.Pipeline!.WorkspaceId == current.WorkspaceId);
        if (deal is null || stage is null) return NotFound("Deal or stage not found in this workspace");

        var previousStageId = deal.StageId;
        deal.StageId = stage.Id;
        deal.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await queue.Enqueue("refresh_reports", new { workspaceId = current.WorkspaceId }, current.WorkspaceId);

        if (previousStageId != stage.Id)
        {
            await webhooks.Enqueue(current.WorkspaceId, "deal.stage_changed",
                new { dealId = deal.Id, previousStageId, stageId = stage.Id });
            if (stage.IsWon)
                await webhooks.Enqueue(current.WorkspaceId, "deal.won", new { dealId = deal.Id, stageId = stage.Id });
            else if (stage.IsLost)
                await webhooks.Enqueue(current.WorkspaceId, "deal.lost", new { dealId = deal.Id, stageId = stage.Id });
        }
        return NoContent();
    }

    [HttpPost("deals/{dealId}/score")]
    public async Task<ActionResult<ScoreDealResponse>> ScoreDeal(string dealId, [FromServices] JobQueueService queue)
    {
        var deal = await db.Deals.FirstOrDefaultAsync(d => d.Id == dealId && d.WorkspaceId == current.WorkspaceId);
        if (deal is null) return NotFound();

        var jobId = await queue.Enqueue("score_deal", new { dealId = deal.Id, workspaceId = current.WorkspaceId }, current.WorkspaceId);
        return Ok(new ScoreDealResponse(jobId));
    }

    [HttpPost("deals/{dealId}/draft-email")]
    public async Task<ActionResult<EmailDraftResponse>> DraftEmail(string dealId, DraftEmailRequest request, [FromServices] JobQueueService queue)
    {
        var deal = await db.Deals.FirstOrDefaultAsync(d => d.Id == dealId && d.WorkspaceId == current.WorkspaceId);
        if (deal is null) return NotFound();

        var jobId = await queue.Enqueue(
            "draft_email",
            new { dealId = deal.Id, workspaceId = current.WorkspaceId, instruction = request.Instruction },
            current.WorkspaceId);
        return Ok(new EmailDraftResponse(jobId));
    }

    [HttpGet("deals/{dealId}")]
    public async Task<ActionResult<DealDetailDto>> GetDeal(string dealId)
    {
        var deal = await db.Deals
            .Include(d => d.Stage)
            .Include(d => d.Company)
            .Include(d => d.Contacts)
            .Include(d => d.Activities.OrderByDescending(a => a.CreatedAt))
            .FirstOrDefaultAsync(d => d.Id == dealId && d.WorkspaceId == current.WorkspaceId && d.DeletedAt == null);
        if (deal is null) return NotFound();
        return Ok(ToDealDetailDto(deal));
    }

    [HttpPost("deals/{dealId}/summarize")]
    public async Task<ActionResult<SummarizeDealResponse>> SummarizeDeal(string dealId, [FromServices] JobQueueService queue)
    {
        var deal = await db.Deals.FirstOrDefaultAsync(d => d.Id == dealId && d.WorkspaceId == current.WorkspaceId);
        if (deal is null) return NotFound();

        var jobId = await queue.Enqueue("summarize_deal", new { dealId = deal.Id, workspaceId = current.WorkspaceId }, current.WorkspaceId);
        return Ok(new SummarizeDealResponse(jobId));
    }

    [HttpPost("deals/{dealId}/next-best-action")]
    public async Task<ActionResult<NextBestActionResponse>> NextBestAction(string dealId, [FromServices] JobQueueService queue)
    {
        var deal = await db.Deals.FirstOrDefaultAsync(d => d.Id == dealId && d.WorkspaceId == current.WorkspaceId);
        if (deal is null) return NotFound();

        var jobId = await queue.Enqueue(
            "next_best_action",
            new { dealId = deal.Id, workspaceId = current.WorkspaceId },
            current.WorkspaceId,
            requestedByUserId: current.UserId);
        return Ok(new NextBestActionResponse(jobId));
    }

    [HttpPut("deals/{dealId}")]
    public async Task<ActionResult<DealDetailDto>> UpdateDeal(string dealId, UpdateDealRequest request, [FromServices] JobQueueService queue)
    {
        if (!ValidForecastCategories.Contains(request.ForecastCategory))
            return BadRequest("Invalid forecast category");
        if (request.AmountCents is < 0)
            return BadRequest("Amount cannot be negative");

        var deal = await db.Deals
            .Include(d => d.Stage)
            .Include(d => d.Company)
            .Include(d => d.Contacts)
            .Include(d => d.Activities.OrderByDescending(a => a.CreatedAt))
            .FirstOrDefaultAsync(d => d.Id == dealId && d.WorkspaceId == current.WorkspaceId && d.DeletedAt == null);
        if (deal is null) return NotFound();

        var fieldDefs = await db.FieldDefinitions
            .Where(f => f.WorkspaceId == current.WorkspaceId && f.EntityType == "deal")
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

        deal.AmountCents = request.AmountCents;
        deal.Currency = request.Currency;
        deal.ForecastCategory = request.ForecastCategory;
        deal.CustomFields = customFields;
        deal.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await queue.Enqueue("refresh_reports", new { workspaceId = current.WorkspaceId }, current.WorkspaceId);

        return Ok(ToDealDetailDto(deal));
    }

    private static DealDetailDto ToDealDetailDto(Deal deal)
    {
        var activitiesSinceSummary = deal.AiSummarizedAt is { } summarizedAt
            ? deal.Activities.Count(a => a.CreatedAt > summarizedAt)
            : deal.Activities.Count;

        return new DealDetailDto(
            deal.Id,
            deal.Company?.Name ?? "Untitled deal",
            deal.Stage!.Name,
            deal.AmountCents,
            deal.Currency,
            deal.ForecastCategory,
            deal.AiScore,
            deal.AiScoreRationale,
            deal.AiSummary,
            deal.AiSummarizedAt,
            activitiesSinceSummary,
            deal.Contacts.Select(c => string.Join(" ", new[] { c.FirstName, c.LastName }.Where(s => !string.IsNullOrWhiteSpace(s)))).ToList(),
            deal.Activities.Select(a => new ActivityDto(a.Id, a.Type, a.Body, a.CreatedAt)).ToList(),
            JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(deal.CustomFields) ?? []
        );
    }

    [HttpPost("deals/{dealId}/activities")]
    public async Task<IActionResult> LogActivity(string dealId, LogActivityRequest request, [FromServices] JobQueueService queue)
    {
        if (string.IsNullOrWhiteSpace(request.Body)) return BadRequest("Enter some notes");
        if (!ValidActivityTypes.Contains(request.Type)) return BadRequest("Invalid activity type");

        var deal = await db.Deals.FirstOrDefaultAsync(d => d.Id == dealId && d.WorkspaceId == current.WorkspaceId);
        if (deal is null) return NotFound();

        db.Activities.Add(new Activity
        {
            WorkspaceId = current.WorkspaceId,
            DealId = deal.Id,
            CompanyId = deal.CompanyId,
            Type = request.Type,
            Body = request.Body,
        });
        await db.SaveChangesAsync();
        await queue.Enqueue("refresh_reports", new { workspaceId = current.WorkspaceId }, current.WorkspaceId);
        return NoContent();
    }
}
