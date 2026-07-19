namespace CrmApi.Dtos;

// IsSystem roles (owner/admin/member) have Id: null — they're not database
// rows (see Models/Role.cs), just the fixed permission map in
// Authorization/Permissions.cs surfaced the same shape as a custom role.
public record RoleDto(string? Id, string Name, List<string> Permissions, bool IsSystem);

public record CreateRoleRequest(string Name, List<string> Permissions);

public record UpdateRoleRequest(List<string> Permissions);
