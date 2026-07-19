namespace CrmApi.Dtos;

public record SavedViewDto(string Id, string EntityType, string Name, string QueryString, DateTime CreatedAt);

public record CreateSavedViewRequest(string EntityType, string Name, string QueryString);
