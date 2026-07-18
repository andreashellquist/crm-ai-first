using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace CrmApi.Services;

// Resolves the caller's identity from JWT claims. WorkspaceId is embedded at
// login time (the first WorkspaceMember, same as the Next.js walking
// skeleton's requireWorkspace()) — no workspace-switching UI exists yet.
public class CurrentUser(IHttpContextAccessor accessor)
{
    private ClaimsPrincipal Principal =>
        accessor.HttpContext?.User ?? throw new InvalidOperationException("No HTTP context");

    public string UserId => Principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
        ?? throw new UnauthorizedAccessException("Missing sub claim");

    public string WorkspaceId => Principal.FindFirstValue("workspaceId")
        ?? throw new UnauthorizedAccessException("Missing workspaceId claim");

    public string Role => Principal.FindFirstValue("role") ?? "member";
}
