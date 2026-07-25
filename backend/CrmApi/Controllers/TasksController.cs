using CrmApi.Data;
using CrmApi.Dtos;
using CrmApi.Models;
using CrmApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmApi.Controllers;

[ApiController]
[Route("api/tasks")]
[Authorize]
public class TasksController(AppDbContext db, CurrentUser current) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<TaskDto>>> List(
        [FromQuery] string? dealId, [FromQuery] string? contactId, [FromQuery] string? companyId,
        [FromQuery] bool includeCompleted = false)
    {
        var query = db.Tasks.Include(t => t.AssignedToUser).Where(t => t.WorkspaceId == current.WorkspaceId);
        if (!string.IsNullOrWhiteSpace(dealId)) query = query.Where(t => t.DealId == dealId);
        if (!string.IsNullOrWhiteSpace(contactId)) query = query.Where(t => t.ContactId == contactId);
        if (!string.IsNullOrWhiteSpace(companyId)) query = query.Where(t => t.CompanyId == companyId);
        if (!includeCompleted) query = query.Where(t => t.CompletedAt == null);

        var tasks = await query
            .OrderBy(t => t.DueAt == null) // due-dated tasks first
            .ThenBy(t => t.DueAt)
            .ThenByDescending(t => t.CreatedAt)
            .ToListAsync();
        return Ok(tasks.Select(ToDto).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<TaskDto>> Create(CreateTaskRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            return BadRequest("Title is required");

        // Re-validate every referenced id belongs to this workspace before
        // attaching — never trust a client-supplied id is already scoped
        // correctly (backend-api-engineer).
        if (request.ContactId is not null && !await db.Contacts.AnyAsync(c => c.Id == request.ContactId && c.WorkspaceId == current.WorkspaceId))
            return BadRequest("Contact not found in this workspace");
        if (request.CompanyId is not null && !await db.Companies.AnyAsync(c => c.Id == request.CompanyId && c.WorkspaceId == current.WorkspaceId))
            return BadRequest("Company not found in this workspace");
        if (request.DealId is not null && !await db.Deals.AnyAsync(d => d.Id == request.DealId && d.WorkspaceId == current.WorkspaceId))
            return BadRequest("Deal not found in this workspace");
        if (request.AssignedToUserId is not null
            && !await db.WorkspaceMembers.AnyAsync(m => m.UserId == request.AssignedToUserId && m.WorkspaceId == current.WorkspaceId))
            return BadRequest("User is not a member of this workspace");

        var task = new TaskItem
        {
            WorkspaceId = current.WorkspaceId,
            Title = request.Title,
            DueAt = request.DueAt,
            ContactId = request.ContactId,
            CompanyId = request.CompanyId,
            DealId = request.DealId,
            AssignedToUserId = request.AssignedToUserId,
        };
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        if (request.AssignedToUserId is not null)
        {
            var user = await db.Users.FindAsync(request.AssignedToUserId);
            task.AssignedToUser = user;
        }
        return Ok(ToDto(task));
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<TaskDto>> Update(string id, UpdateTaskRequest request)
    {
        var task = await db.Tasks.Include(t => t.AssignedToUser).FirstOrDefaultAsync(t => t.Id == id && t.WorkspaceId == current.WorkspaceId);
        if (task is null) return NotFound();
        if (string.IsNullOrWhiteSpace(request.Title)) return BadRequest("Title is required");
        if (request.AssignedToUserId is not null
            && !await db.WorkspaceMembers.AnyAsync(m => m.UserId == request.AssignedToUserId && m.WorkspaceId == current.WorkspaceId))
            return BadRequest("User is not a member of this workspace");

        task.Title = request.Title;
        task.DueAt = request.DueAt;
        task.CompletedAt = request.Completed ? (task.CompletedAt ?? DateTime.UtcNow) : null;
        task.AssignedToUserId = request.AssignedToUserId;
        await db.SaveChangesAsync();

        task.AssignedToUser = request.AssignedToUserId is null ? null : await db.Users.FindAsync(request.AssignedToUserId);
        return Ok(ToDto(task));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == id && t.WorkspaceId == current.WorkspaceId);
        if (task is null) return NotFound();

        db.Tasks.Remove(task);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private static TaskDto ToDto(TaskItem t) => new(
        t.Id, t.Title, t.DueAt, t.CompletedAt, t.AiSuggested, t.ContactId, t.CompanyId, t.DealId,
        t.AssignedToUserId, t.AssignedToUser?.Name ?? t.AssignedToUser?.Email);
}
