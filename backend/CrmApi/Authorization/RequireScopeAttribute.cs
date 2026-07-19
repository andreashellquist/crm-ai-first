using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CrmApi.Authorization;

// Enforces an API key's granted scope on the public v1 API — the
// scope-based counterpart to RequireRoleAttribute for session auth. Scopes
// come from "scope" claims ApiKeyAuthenticationHandler attaches to the
// ClaimsPrincipal, one per ApiKey.Scopes entry.
public class RequireScopeAttribute(string scope) : Attribute, IAsyncAuthorizationFilter
{
    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var scopes = context.HttpContext.User.FindAll("scope").Select(c => c.Value).ToHashSet();
        if (!scopes.Contains(scope))
        {
            context.Result = new ObjectResult(new { error = $"API key is missing required scope: {scope}" })
            {
                StatusCode = StatusCodes.Status403Forbidden,
            };
        }
        return Task.CompletedTask;
    }
}
