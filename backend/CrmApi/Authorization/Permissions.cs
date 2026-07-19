namespace CrmApi.Authorization;

// Fixed permission catalog for custom roles (auth-security-expert's
// "Custom roles" design note) — a permission set attached to a role, not
// one-off boolean flags on WorkspaceMember. The three system roles
// (owner/admin/member) map to fixed permission sets here rather than
// database rows, so this is purely additive: every existing workspace's
// behavior is unchanged unless it creates a custom role.
public static class Permissions
{
    public const string ManageSettings = "settings:manage";
    public const string ManageFields = "fields:manage";
    public const string ManageApiKeys = "api_keys:manage";
    public const string ManageWebhooks = "webhooks:manage";
    public const string ManageMembers = "members:manage";
    public const string ManageRoles = "roles:manage";

    public static readonly string[] All =
    [
        ManageSettings, ManageFields, ManageApiKeys, ManageWebhooks, ManageMembers, ManageRoles,
    ];

    // System role names a custom role's Name must never collide with —
    // RolesController enforces this so "owner"/"admin"/"member" always
    // resolve to the fixed map below, never a workspace-editable row.
    public static readonly string[] SystemRoleNames = ["owner", "admin", "member"];

    private static readonly Dictionary<string, string[]> SystemRolePermissions = new()
    {
        ["owner"] = All,
        // Admin manages day-to-day workspace configuration but not the
        // role/permission system itself — only an owner can grant
        // permissions, so an admin can never escalate a custom role (or
        // themselves) beyond what an owner already allowed.
        ["admin"] = [ManageSettings, ManageFields, ManageApiKeys, ManageWebhooks, ManageMembers],
        ["member"] = [],
    };

    public static bool SystemRoleHas(string roleName, string permission) =>
        SystemRolePermissions.TryGetValue(roleName, out var granted) && granted.Contains(permission);
}
