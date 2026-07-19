using CrmApi.Data;
using CrmApi.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace CrmApi.Authorization;

// The permission-based counterpart to RequireRoleAttribute — checks a
// CurrentUser.Role name against the fixed system-role permission map first
// (no DB lookup, matches the vast majority of requests since every
// workspace starts with only owner/admin/member), falling back to a custom
// Role row lookup only when the role name isn't one of the three system
// names. Requires [Authorize] on the same action/controller, same as
// RequireRoleAttribute.
public class RequirePermissionAttribute(string permission) : Attribute, IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var current = context.HttpContext.RequestServices.GetRequiredService<CurrentUser>();

        bool granted;
        if (Permissions.SystemRoleHas(current.Role, permission))
        {
            granted = true;
        }
        else
        {
            var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
            var customRole = await db.Roles
                .FirstOrDefaultAsync(r => r.WorkspaceId == current.WorkspaceId && r.Name == current.Role);
            granted = customRole?.Permissions.Contains(permission) ?? false;
        }

        if (!granted)
        {
            context.Result = new ObjectResult(new { error = $"Requires permission: {permission}" })
            {
                StatusCode = StatusCodes.Status403Forbidden,
            };
        }
    }
}
