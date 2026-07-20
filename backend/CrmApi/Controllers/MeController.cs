using CrmApi.Data;
using CrmApi.Dtos;
using CrmApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmApi.Controllers;

// Self-service profile — the first reader of WorkspaceMember.Timezone, a
// column that existed since Phase 0 but nothing ever set or read (see the
// i18n-currency-timezone skill). Scoped to the caller's own membership row
// in the current workspace; there is no "view another user's profile"
// endpoint here — that's MembersController's roster view.
[ApiController]
[Route("api/me")]
[Authorize]
public class MeController(AppDbContext db, CurrentUser current) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<MeDto>> Get()
    {
        var member = await db.WorkspaceMembers.Include(m => m.User)
            .FirstOrDefaultAsync(m => m.WorkspaceId == current.WorkspaceId && m.UserId == current.UserId);
        if (member is null) return NotFound();

        return Ok(new MeDto(member.UserId, member.User!.Name, member.User.Email, member.Role, member.Timezone));
    }

    [HttpPut]
    public async Task<ActionResult<MeDto>> Update(UpdateMeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Timezone)) return BadRequest("Timezone is required");
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(request.Timezone);
        }
        catch (TimeZoneNotFoundException)
        {
            return BadRequest($"Unknown timezone \"{request.Timezone}\" — expected an IANA identifier, e.g. \"America/New_York\"");
        }

        var member = await db.WorkspaceMembers.Include(m => m.User)
            .FirstOrDefaultAsync(m => m.WorkspaceId == current.WorkspaceId && m.UserId == current.UserId);
        if (member is null) return NotFound();

        member.User!.Name = request.Name;
        member.Timezone = request.Timezone;
        member.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return Ok(new MeDto(member.UserId, member.User.Name, member.User.Email, member.Role, member.Timezone));
    }
}
