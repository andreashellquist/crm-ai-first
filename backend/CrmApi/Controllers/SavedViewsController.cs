using CrmApi.Data;
using CrmApi.Dtos;
using CrmApi.Models;
using CrmApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmApi.Controllers;

[ApiController]
[Route("api/saved-views")]
[Authorize]
public class SavedViewsController(AppDbContext db, CurrentUser current) : ControllerBase
{
    // v1-scoped to the contacts list — see Models/SavedView.cs and the
    // "Search & saved views" entry in docs/PRODUCT_SCOPE.md.
    private static readonly string[] ValidEntityTypes = ["contact"];

    // Personal, not workspace-shared — every query filters by current.UserId
    // as well as current.WorkspaceId (same reasoning as notifications: a
    // teammate must never read or delete another user's saved views).
    [HttpGet]
    public async Task<ActionResult<List<SavedViewDto>>> List([FromQuery] string entityType)
    {
        var views = await db.SavedViews
            .Where(v => v.WorkspaceId == current.WorkspaceId && v.UserId == current.UserId && v.EntityType == entityType)
            .OrderByDescending(v => v.CreatedAt)
            .ToListAsync();
        return Ok(views.Select(ToDto).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<SavedViewDto>> Create(CreateSavedViewRequest request)
    {
        if (!ValidEntityTypes.Contains(request.EntityType)) return BadRequest("Invalid entity type");
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest("Name is required");

        var view = new SavedView
        {
            WorkspaceId = current.WorkspaceId,
            UserId = current.UserId,
            EntityType = request.EntityType,
            Name = request.Name,
            QueryString = request.QueryString,
        };
        db.SavedViews.Add(view);
        await db.SaveChangesAsync();
        return Ok(ToDto(view));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var view = await db.SavedViews.FirstOrDefaultAsync(v =>
            v.Id == id && v.WorkspaceId == current.WorkspaceId && v.UserId == current.UserId);
        if (view is null) return NotFound();

        db.SavedViews.Remove(view);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private static SavedViewDto ToDto(SavedView v) => new(v.Id, v.EntityType, v.Name, v.QueryString, v.CreatedAt);
}
