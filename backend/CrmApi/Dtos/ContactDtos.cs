namespace CrmApi.Dtos;

public record ContactDto(
    string Id,
    string? FirstName,
    string? LastName,
    string? Email,
    string? CompanyName,
    string LifecycleStage
);

public record CreateContactRequest(string FirstName, string? LastName, string? Email, string? CompanyName);
