namespace CrmApi.Dtos;

public record MemberDto(string Id, string UserId, string? Name, string Email, string Role, bool IsActive, DateTime CreatedAt);

public record UpdateMemberRoleRequest(string Role);
