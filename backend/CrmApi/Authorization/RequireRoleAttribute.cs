using CrmApi.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CrmApi.Authorization;

// Enforces workspace role at the point of mutation, not just what the UI
// renders — see auth-security-expert: "a hidden button is not access
// control." Requires [Authorize] on the same action/controller so a
// CurrentUser is resolvable; runs after model binding but before the action
// body, same as any other authorization filter.
public class RequireRoleAttribute(params string[] roles) : Attribute, IAsyncAuthorizationFilter
{
    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var current = context.HttpContext.RequestServices.GetRequiredService<CurrentUser>();
        if (!roles.Contains(current.Role))
        {
            context.Result = new ObjectResult(new { error = $"Requires one of these roles: {string.Join(", ", roles)}" })
            {
                StatusCode = StatusCodes.Status403Forbidden,
            };
        }
        return Task.CompletedTask;
    }
}
