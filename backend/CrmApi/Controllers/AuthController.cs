using CrmApi.Data;
using CrmApi.Dtos;
using CrmApi.Models;
using CrmApi.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmApi.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(AppDbContext db, JwtService jwt, ILogger<AuthController> logger) : ControllerBase
{
    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == request.Email);
        if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return Unauthorized();

        var membership = await FirstMembershipAsync(user.Id);
        if (membership?.Workspace is null) return Unauthorized();

        var token = jwt.GenerateToken(user.Id, membership.WorkspaceId, membership.Role);
        return Ok(new LoginResponse(token, membership.WorkspaceId, membership.Workspace.Name));
    }

    // SCAFFOLD ONLY — see GoogleOAuthClient. Called by the Next.js OAuth
    // callback route (server-to-server), never the browser directly: the
    // browser's redirect dance with Google terminates at the frontend, which
    // forwards the authorization code here to exchange it, preserving "the
    // browser never talks to the .NET API directly" (auth-security-expert).
    [HttpPost("google/exchange")]
    public async Task<ActionResult<LoginResponse>> GoogleExchange(GoogleExchangeRequest request, [FromServices] IGoogleOAuthClient google)
    {
        GoogleUserInfo userInfo;
        try
        {
            var tokens = await google.ExchangeCodeAsync(request.Code);
            userInfo = await google.GetUserInfoAsync(tokens.AccessToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "oauth_google exchange_failed");
            return Unauthorized("Google sign-in failed");
        }

        if (!userInfo.EmailVerified) return Unauthorized("Google account email is not verified");

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == userInfo.Email);
        if (user is null)
        {
            // First-time OAuth sign-in — provision a user + a fresh workspace,
            // mirroring typical self-serve signup (no invite flow yet). The
            // password hash is a real bcrypt hash of an unguessable random
            // value: PasswordHash is required and BCrypt.Verify throws on a
            // non-bcrypt-format string, so this keeps the column valid while
            // guaranteeing password login can never succeed for this user.
            user = new User
            {
                Email = userInfo.Email,
                Name = userInfo.Name,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString("N")),
            };
            db.Users.Add(user);

            var workspaceName = string.IsNullOrWhiteSpace(userInfo.Name) ? $"{userInfo.Email}'s Workspace" : $"{userInfo.Name}'s Workspace";
            var workspace = new Workspace { Name = workspaceName };
            db.Workspaces.Add(workspace);
            db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = workspace.Id, UserId = user.Id, Role = "owner" });
            await db.SaveChangesAsync();

            logger.LogInformation("oauth_google provisioned_new_user userId={UserId} workspaceId={WorkspaceId}", user.Id, workspace.Id);
        }

        var membership = await FirstMembershipAsync(user.Id);
        if (membership?.Workspace is null) return Unauthorized();

        var token = jwt.GenerateToken(user.Id, membership.WorkspaceId, membership.Role);
        return Ok(new LoginResponse(token, membership.WorkspaceId, membership.Workspace.Name));
    }

    // First membership only — no workspace-switching UI yet, matches the
    // Next.js walking skeleton's requireWorkspace() behavior.
    private Task<WorkspaceMember?> FirstMembershipAsync(string userId) =>
        db.WorkspaceMembers
            .Include(m => m.Workspace)
            .Where(m => m.UserId == userId)
            .OrderBy(m => m.CreatedAt)
            .FirstOrDefaultAsync();
}
