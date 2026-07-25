using System.Text.Json;

namespace CrmApi.Dtos;

public record ContactDto(
    string Id,
    string? FirstName,
    string? LastName,
    string? Email,
    string? CompanyName,
    string LifecycleStage,
    Dictionary<string, JsonElement> CustomFields
);

public record CreateContactRequest(
    string FirstName,
    string? LastName,
    string? Email,
    string? CompanyName,
    Dictionary<string, JsonElement>? CustomFields = null
);

// Data-subject access request (GDPR/CCPA "what do you have on me") response
// — everything this CRM holds that's attributable to this contact. See
// auth-security-expert's "Data-subject requests" section.
public record ContactExportDto(
    string ContactId,
    string? FirstName,
    string? LastName,
    string? Email,
    string? Phone,
    string LifecycleStage,
    string? CompanyName,
    Dictionary<string, JsonElement> CustomFields,
    List<ContactExportActivityDto> Activities,
    List<ContactExportDealDto> Deals,
    DateTime ExportedAt
);

public record ContactExportActivityDto(string Id, string Type, string? Body, DateTime CreatedAt);

public record ContactExportDealDto(string Id, string? CompanyName, int? AmountCents, string? Currency, DateTime CreatedAt);

// Routine detail-page viewing — deliberately separate from ContactExportDto:
// that endpoint writes a contact.exported AuditLog entry, which must only
// fire for real data-subject access requests, not every time someone opens
// a contact's page.
public record ContactDetailDto(
    string Id,
    string? FirstName,
    string? LastName,
    string? Email,
    string? Phone,
    string LifecycleStage,
    string? CompanyId,
    string? CompanyName,
    Dictionary<string, JsonElement> CustomFields,
    List<ContactDetailActivityDto> Activities,
    List<ContactDetailDealDto> Deals
);

public record ContactDetailActivityDto(string Id, string Type, string? Body, DateTime CreatedAt);

public record ContactDetailDealDto(string Id, string Title, string StageName, int? AmountCents, string? Currency);
