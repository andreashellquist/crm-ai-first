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
            )).ToList()
        );
        return Ok(dto);
    }

    [HttpPost("deals/{dealId}/move")]
    public async Task<IActionResult> MoveDeal(string dealId, MoveDealRequest request)
    {
        // Re-validate both IDs belong to this workspace before writing — never
        // trust a client-supplied ID is already scoped correctly.
        var deal = await db.Deals.FirstOrDefaultAsync(d => d.Id == dealId && d.WorkspaceId == current.WorkspaceId);
        var stage = await db.Stages.Include(s => s.Pipeline)
            .FirstOrDefaultAsync(s => s.Id == request.StageId && s.Pipeline!.WorkspaceId == current.WorkspaceId);
        if (deal is null || stage is null) return NotFound("Deal or stage not found in this workspace");

        deal.StageId = stage.Id;
        deal.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
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

        var dto = new DealDetailDto(
            deal.Id,
            deal.Company?.Name ?? "Untitled deal",
            deal.Stage!.Name,
            deal.AmountCents,
            deal.Currency,
            deal.AiScore,
            deal.AiScoreRationale,
            deal.Contacts.Select(c => string.Join(" ", new[] { c.FirstName, c.LastName }.Where(s => !string.IsNullOrWhiteSpace(s)))).ToList(),
            deal.Activities.Select(a => new ActivityDto(a.Id, a.Type, a.Body, a.CreatedAt)).ToList()
        );
        return Ok(dto);
    }

    [HttpPost("deals/{dealId}/activities")]
    public async Task<IActionResult> LogActivity(string dealId, LogActivityRequest request)
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
        return NoContent();
    }
}
