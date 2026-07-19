namespace CrmApi.Models;

// A workspace-defined custom role — see Authorization/Permissions.cs. The
// three system roles (owner/admin/member) are deliberately NOT rows in this
// table: their permission sets are a fixed, hardcoded mapping in code
// (Permissions.SystemRoleHas), so every already-provisioned workspace's
// WorkspaceMember.Role keeps working unchanged — this table only holds the
// additive, workspace-specific roles a workspace explicitly creates.
public class Role
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string WorkspaceId { get; set; }
    public required string Name { get; set; } // unique per workspace; must not collide with "owner"/"admin"/"member"
    public List<string> Permissions { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Workspace? Workspace { get; set; }
}
