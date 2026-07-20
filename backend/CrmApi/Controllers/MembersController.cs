using CrmApi.Authorization;
using CrmApi.Data;
using CrmApi.Dtos;
using CrmApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmApi.Controllers;

// Workspace member roster + role assignment — the piece that makes custom
// roles (RolesController) actually usable: a role only matters once it's
// assigned to someone. Viewing teammates is open to any authenticated
// member (matches ApiKeysController/WebhookSubscriptionsController's
// read/write split); changing a role or removing a member needs
// members:manage.
[ApiController]
[Route("api/members")]
[Authorize]
public class MembersController(AppDbContext db, CurrentUser current, AuditLogService audit) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<MemberDto>>> List()
    {
        var members = await db.WorkspaceMembers.Include(m => m.User)
            .Where(m => m.WorkspaceId == current.WorkspaceId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();

        return Ok(members.Select(m => new MemberDto(m.Id, m.UserId, m.User!.Name, m.User.Email, m.Role, m.IsActive, m.CreatedAt)).ToList());
    }

    [HttpPut("{id}/role")]
    [RequirePermission(Permissions.ManageMembers)]
    public async Task<ActionResult<MemberDto>> UpdateRole(string id, UpdateMemberRoleRequest request)
    {
        var member = await db.WorkspaceMembers.Include(m => m.User)
            .FirstOrDefaultAsync(m => m.Id == id && m.WorkspaceId == current.WorkspaceId);
        if (member is null) return NotFound();

        if (string.IsNullOrWhiteSpace(request.Role)) return BadRequest("Role is required");
        if (!Permissions.SystemRoleNames.Contains(request.Role))
        {
            var customRoleExists = await db.Roles.AnyAsync(r => r.WorkspaceId == current.WorkspaceId && r.Name == request.Role);
            if (!customRoleExists) return BadRequest($"Unknown role \"{request.Role}\"");
        }

        if (member.Role == "owner" && request.Role != "owner" && await IsOnlyOwnerAsync())
            return Conflict("Can't change the workspace's only owner — promote another member to owner first");

        var previousRole = member.Role;
        member.Role = request.Role;
        member.UpdatedAt = DateTime.UtcNow;
        audit.Log(current.WorkspaceId, current.UserId, AuditLogService.Actions.MemberRoleChanged, "WorkspaceMember", member.Id,
            new { previousRole, newRole = member.Role, targetUserId = member.UserId });
        await db.SaveChangesAsync();
        return Ok(new MemberDto(member.Id, member.UserId, member.User!.Name, member.User.Email, member.Role, member.IsActive, member.CreatedAt));
    }

    [HttpDelete("{id}")]
    [RequirePermission(Permissions.ManageMembers)]
    public async Task<IActionResult> Remove(string id)
    {
        var member = await db.WorkspaceMembers.FirstOrDefaultAsync(m => m.Id == id && m.WorkspaceId == current.WorkspaceId);
        if (member is null) return NotFound();

        if (member.Role == "owner" && await IsOnlyOwnerAsync())
            return Conflict("Can't remove the workspace's only owner — promote another member to owner first");

        audit.Log(current.WorkspaceId, current.UserId, AuditLogService.Actions.MemberRemoved, "WorkspaceMember", member.Id,
            new { targetUserId = member.UserId, role = member.Role });
        db.WorkspaceMembers.Remove(member);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private async Task<bool> IsOnlyOwnerAsync()
    {
        var ownerCount = await db.WorkspaceMembers.CountAsync(m => m.WorkspaceId == current.WorkspaceId && m.Role == "owner");
        return ownerCount <= 1;
    }
}
