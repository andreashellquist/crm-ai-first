using CrmApi.Authorization;
using CrmApi.Data;
using CrmApi.Dtos;
using CrmApi.Models;
using CrmApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmApi.Controllers;

// Custom roles (auth-security-expert's "Custom roles" design note) — the
// owner/admin/member system roles are a fixed permission map
// (Authorization/Permissions.cs), not rows here; this controller only
// manages the additive, workspace-defined roles on top of them. Creating or
// changing a role is itself gated by the roles:manage permission, which
// only the owner system role carries by default — an admin (or any custom
// role) can never grant itself more access than an owner already allowed.
[ApiController]
[Route("api/roles")]
[Authorize]
public class RolesController(AppDbContext db, CurrentUser current, AuditLogService audit) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<RoleDto>>> List()
    {
        var systemRoles = Permissions.SystemRoleNames
            .Select(name => new RoleDto(null, name, Permissions.All.Where(p => Permissions.SystemRoleHas(name, p)).ToList(), true));

        var customRoles = await db.Roles
            .Where(r => r.WorkspaceId == current.WorkspaceId)
            .OrderBy(r => r.Name)
            .Select(r => new RoleDto(r.Id, r.Name, r.Permissions, false))
            .ToListAsync();

        return Ok(systemRoles.Concat(customRoles).ToList());
    }

    [HttpPost]
    [RequirePermission(Permissions.ManageRoles)]
    public async Task<ActionResult<RoleDto>> Create(CreateRoleRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest("Name is required");
        if (Permissions.SystemRoleNames.Contains(request.Name))
            return BadRequest($"\"{request.Name}\" is a system role name and can't be reused");

        var permissions = (request.Permissions ?? []).Distinct().ToList();
        var invalid = permissions.Except(Permissions.All).ToList();
        if (invalid.Count > 0) return BadRequest($"Unknown permission(s): {string.Join(", ", invalid)}");

        var exists = await db.Roles.AnyAsync(r => r.WorkspaceId == current.WorkspaceId && r.Name == request.Name);
        if (exists) return Conflict($"A role named \"{request.Name}\" already exists");

        var role = new Role { WorkspaceId = current.WorkspaceId, Name = request.Name, Permissions = permissions };
        db.Roles.Add(role);
        audit.Log(current.WorkspaceId, current.UserId, AuditLogService.Actions.RoleCreated, "Role", role.Id,
            new { name = role.Name, permissions = role.Permissions });
        await db.SaveChangesAsync();
        return Ok(new RoleDto(role.Id, role.Name, role.Permissions, false));
    }

    [HttpPut("{id}")]
    [RequirePermission(Permissions.ManageRoles)]
    public async Task<ActionResult<RoleDto>> Update(string id, UpdateRoleRequest request)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == id && r.WorkspaceId == current.WorkspaceId);
        if (role is null) return NotFound();

        var permissions = (request.Permissions ?? []).Distinct().ToList();
        var invalid = permissions.Except(Permissions.All).ToList();
        if (invalid.Count > 0) return BadRequest($"Unknown permission(s): {string.Join(", ", invalid)}");

        role.Permissions = permissions;
        role.UpdatedAt = DateTime.UtcNow;
        audit.Log(current.WorkspaceId, current.UserId, AuditLogService.Actions.RoleUpdated, "Role", role.Id,
            new { name = role.Name, permissions = role.Permissions });
        await db.SaveChangesAsync();
        return Ok(new RoleDto(role.Id, role.Name, role.Permissions, false));
    }

    [HttpDelete("{id}")]
    [RequirePermission(Permissions.ManageRoles)]
    public async Task<IActionResult> Delete(string id)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == id && r.WorkspaceId == current.WorkspaceId);
        if (role is null) return NotFound();

        var inUse = await db.WorkspaceMembers.AnyAsync(m => m.WorkspaceId == current.WorkspaceId && m.Role == role.Name);
        if (inUse) return Conflict($"\"{role.Name}\" is still assigned to at least one member — reassign them first");

        audit.Log(current.WorkspaceId, current.UserId, AuditLogService.Actions.RoleDeleted, "Role", role.Id, new { name = role.Name });
        db.Roles.Remove(role);
        await db.SaveChangesAsync();
        return NoContent();
    }
}
