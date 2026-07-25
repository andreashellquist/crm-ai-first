using CrmApi.Data;
using CrmApi.Dtos;
using CrmApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmApi.Controllers;

[ApiController]
[Route("api/ask")]
[Authorize]
public class AskController(AppDbContext db, CurrentUser current) : ControllerBase
{
    // Like every other LLM call in this app, this goes through the job
    // queue rather than blocking the request — see JobWorker's "rag_query"
    // handler and CLAUDE.md's "Every LLM call... must go through" note.
    [HttpPost]
    public async Task<ActionResult<AskQuestionResponse>> Ask(AskQuestionRequest request, [FromServices] JobQueueService queue)
    {
        if (string.IsNullOrWhiteSpace(request.Question)) return BadRequest("Question is required");

        // At most one scope hint — a question is either asked from a
        // specific record's context or it's a workspace-wide search, never
        // both (RagQueryService.RetrieveActivities only looks at the first
        // non-null one, so validate that up front rather than silently
        // ignoring the others).
        var scopeCount = new[] { request.DealId, request.ContactId, request.CompanyId }.Count(id => id is not null);
        if (scopeCount > 1) return BadRequest("Provide at most one of dealId, contactId, companyId");

        if (request.DealId is { } dealId && !await db.Deals.AnyAsync(d => d.Id == dealId && d.WorkspaceId == current.WorkspaceId && d.DeletedAt == null))
            return NotFound("Deal not found");
        if (request.ContactId is { } contactId && !await db.Contacts.AnyAsync(c => c.Id == contactId && c.WorkspaceId == current.WorkspaceId && c.DeletedAt == null))
            return NotFound("Contact not found");
        if (request.CompanyId is { } companyId && !await db.Companies.AnyAsync(c => c.Id == companyId && c.WorkspaceId == current.WorkspaceId && c.DeletedAt == null))
            return NotFound("Company not found");

        var jobId = await queue.Enqueue(
            "rag_query",
            new { question = request.Question, workspaceId = current.WorkspaceId, dealId = request.DealId, contactId = request.ContactId, companyId = request.CompanyId },
            current.WorkspaceId);
        return Ok(new AskQuestionResponse(jobId));
    }
}
