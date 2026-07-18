namespace CrmApi.Dtos;

public record LoginRequest(string Email, string Password);

public record LoginResponse(string Token, string WorkspaceId, string WorkspaceName);

// Server-to-server only — called by the Next.js OAuth callback route
// handler, never the browser directly (see auth-security-expert: the API
// never trusts a client-supplied identity, and the browser never talks to
// the .NET API directly).
public record GoogleExchangeRequest(string Code);
