using CrmApi.Data;
using CrmApi.Dtos;
using CrmApi.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmApi.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(AppDbContext db, JwtService jwt) : ControllerBase
{
    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == request.Email);
        if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return Unauthorized();

        // First membership only — no workspace-switching UI yet, matches the
        // Next.js walking skeleton's requireWorkspace() behavior.
        var membership = await db.WorkspaceMembers
            .Include(m => m.Workspace)
            .Where(m => m.UserId == user.Id)
            .OrderBy(m => m.CreatedAt)
            .FirstOrDefaultAsync();
        if (membership?.Workspace is null) return Unauthorized();

        var token = jwt.GenerateToken(user.Id, membership.WorkspaceId, membership.Role);
        return Ok(new LoginResponse(token, membership.WorkspaceId, membership.Workspace.Name));
    }
}
