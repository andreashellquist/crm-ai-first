using System.Text.Json;

namespace CrmApi.Dtos;

public record WorkspaceSettingsDto(Dictionary<string, JsonElement> Terminology, List<string> EnabledModules);

public record UpdateWorkspaceSettingsRequest(Dictionary<string, JsonElement>? Terminology, List<string>? EnabledModules);

public record FieldDefinitionDto(
    string Id,
    string EntityType,
    string Key,
    string Label,
    string FieldType,
    List<string>? Options,
    bool Required,
    int Order
);

public record CreateFieldDefinitionRequest(
    string EntityType,
    string Key,
    string Label,
    string FieldType,
    List<string>? Options,
    bool Required,
    int Order
);

public record UpdateFieldDefinitionRequest(string Label, List<string>? Options, bool Required, int Order);
