namespace CrmApi.Dtos;

// dealId/contactId/companyId are mutually exclusive scoping hints — see
// RagQueryService.RetrieveActivities. All null means a workspace-wide
// keyword-matched question ("what have we discussed with Acme about
// pricing").
public record AskQuestionRequest(string Question, string? DealId, string? ContactId, string? CompanyId);

public record AskQuestionResponse(string JobId);

public record RagCitationDto(string ActivityId, string Type, string Snippet, DateTime CreatedAt, string? DealId, string? ContactId, string? CompanyId);

public record RagAnswerDto(string Answer, List<RagCitationDto> Citations);
