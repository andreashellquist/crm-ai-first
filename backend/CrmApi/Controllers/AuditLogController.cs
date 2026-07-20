using CrmApi.Authorization;
using CrmApi.Data;
using CrmApi.Dtos;
using CrmApi.Models;
using CrmApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmApi.Controllers;

// Read-only view onto AuditLogService's trail — see that class for the
// fixed action catalog and the controllers that write it. Owner/admin only
// to *view*, unlike MembersController's roster (open to any member): this
// surfaces who changed what security-relevant configuration and when, which
// is a more sensitive read than "who's on the team." Capped at the most
// recent 100 entries, same v1 pagination choice as the public API
// (public-api-and-webhooks skill) — real cursor pagination is a follow-up
// once a workspace's trail is long enough to need it.
[ApiController]
[Route("api/audit-log")]
[Authorize]
[RequireRole("owner", "admin")]
public class AuditLogController(AppDbContext db, CurrentUser current) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<AuditLogDto>>> List()
    {
        var logs = await db.AuditLogs.Include(a => a.ActorUser)
            .Where(a => a.WorkspaceId == current.WorkspaceId)
            .OrderByDescending(a => a.CreatedAt)
            .Take(100)
            .ToListAsync();

        return Ok(logs.Select(ToDto).ToList());
    }

    private static AuditLogDto ToDto(AuditLog a) =>
        new(a.Id, a.ActorUserId, a.ActorUser?.Name, a.ActorUser?.Email, a.Action, a.TargetType, a.TargetId, a.Metadata, a.CreatedAt);
}
