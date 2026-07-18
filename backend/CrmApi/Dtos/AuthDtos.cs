namespace CrmApi.Dtos;

public record LoginRequest(string Email, string Password);

public record LoginResponse(string Token, string WorkspaceId, string WorkspaceName);
