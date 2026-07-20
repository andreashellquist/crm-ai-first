namespace CrmApi.Dtos;

public record MeDto(string UserId, string? Name, string Email, string Role, string Timezone);

public record UpdateMeRequest(string? Name, string Timezone);
