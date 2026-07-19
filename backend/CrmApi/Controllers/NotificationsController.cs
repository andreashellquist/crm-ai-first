using CrmApi.Data;
using CrmApi.Dtos;
using CrmApi.Models;
using CrmApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmApi.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationsController(AppDbContext db, CurrentUser current) : ControllerBase
{
    // Notifications are personal, not just workspace-scoped — every query
    // below filters by current.UserId as well as current.WorkspaceId, or a
    // teammate in the same workspace could read/mark another user's
    // notifications. See MultiTenantIsolationTests and
    // NotificationsControllerTests for the regression guards.
    private const int ListLimit = 50;

    [HttpGet]
    public async Task<ActionResult<List<NotificationDto>>> List([FromQuery] bool unreadOnly = false)
    {
        var query = db.Notifications.Where(n => n.WorkspaceId == current.WorkspaceId && n.UserId == current.UserId);
        if (unreadOnly) query = query.Where(n => n.ReadAt == null);

        var notifications = await query
            .OrderByDescending(n => n.CreatedAt)
            .Take(ListLimit)
            .ToListAsync();
        return Ok(notifications.Select(ToDto).ToList());
    }

    [HttpGet("unread-count")]
    public async Task<ActionResult<UnreadCountResponse>> UnreadCount()
    {
        var count = await db.Notifications.CountAsync(n =>
            n.WorkspaceId == current.WorkspaceId && n.UserId == current.UserId && n.ReadAt == null);
        return Ok(new UnreadCountResponse(count));
    }

    [HttpPost("{id}/read")]
    public async Task<IActionResult> MarkRead(string id)
    {
        var notification = await db.Notifications.FirstOrDefaultAsync(n =>
            n.Id == id && n.WorkspaceId == current.WorkspaceId && n.UserId == current.UserId);
        if (notification is null) return NotFound();

        notification.ReadAt ??= DateTime.UtcNow;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead()
    {
        await db.Notifications
            .Where(n => n.WorkspaceId == current.WorkspaceId && n.UserId == current.UserId && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, DateTime.UtcNow));
        return NoContent();
    }

    private static NotificationDto ToDto(Notification n) => new(n.Id, n.Type, n.EntityType, n.EntityId, n.ReadAt, n.CreatedAt);
}
