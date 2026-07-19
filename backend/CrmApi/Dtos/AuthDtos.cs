namespace CrmApi.Dtos;

public record LoginRequest(string Email, string Password);

public record LoginResponse(string Token, string WorkspaceId, string WorkspaceName);

// TemplateId picks a vertical starter template (see VerticalTemplates) —
// applied once, at signup, via WorkspaceProvisioningService.
public record RegisterRequest(string Email, string Password, string Name, string WorkspaceName, string TemplateId);

public record VerticalTemplateSummaryDto(
    string Id,
    string Name,
    string Description,
    string DealTerm,
    string DealTermPlural,
    string CompanyTerm,
    string ContactTerm,
    List<string> Stages
);

// Server-to-server only — called by the Next.js OAuth callback route
// handler, never the browser directly (see auth-security-expert: the API
// never trusts a client-supplied identity, and the browser never talks to
// the .NET API directly).
public record GoogleExchangeRequest(string Code);
