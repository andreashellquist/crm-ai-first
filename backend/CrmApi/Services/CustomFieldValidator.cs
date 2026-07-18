using System.Text.Json;
using CrmApi.Models;

namespace CrmApi.Services;

public class CustomFieldValidationException(string message) : Exception(message);

// Validates a request's customFields object against that workspace's
// FieldDefinition rows for the given entity type — see the
// workspace-customization skill. This is the only thing standing between "a
// field this workspace actually configured" and arbitrary client-supplied
// JSON landing in a jsonb column, so every entity create/update that accepts
// customFields must route through this rather than persisting the raw input.
public static class CustomFieldValidator
{
    public static string ValidateAndSerialize(List<FieldDefinition> definitions, Dictionary<string, JsonElement>? input)
    {
        input ??= [];
        var byKey = definitions.ToDictionary(d => d.Key);

        foreach (var key in input.Keys)
        {
            if (!byKey.ContainsKey(key))
                throw new CustomFieldValidationException($"Unknown custom field \"{key}\"");
        }

        var result = new Dictionary<string, JsonElement>();
        foreach (var def in definitions)
        {
            if (!input.TryGetValue(def.Key, out var value))
            {
                if (def.Required)
                    throw new CustomFieldValidationException($"\"{def.Label}\" is required");
                continue;
            }
            ValidateType(def, value);
            result[def.Key] = value;
        }

        return JsonSerializer.Serialize(result);
    }

    private static void ValidateType(FieldDefinition def, JsonElement value)
    {
        switch (def.FieldType)
        {
            case "text":
                if (value.ValueKind != JsonValueKind.String)
                    throw new CustomFieldValidationException($"\"{def.Label}\" must be text");
                break;
            case "number":
                if (value.ValueKind != JsonValueKind.Number)
                    throw new CustomFieldValidationException($"\"{def.Label}\" must be a number");
                break;
            case "boolean":
                if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    throw new CustomFieldValidationException($"\"{def.Label}\" must be true or false");
                break;
            case "date":
                if (value.ValueKind != JsonValueKind.String || !DateTime.TryParse(value.GetString(), out _))
                    throw new CustomFieldValidationException($"\"{def.Label}\" must be a valid date");
                break;
            case "select":
                var options = string.IsNullOrWhiteSpace(def.Options)
                    ? []
                    : JsonSerializer.Deserialize<List<string>>(def.Options) ?? [];
                if (value.ValueKind != JsonValueKind.String || !options.Contains(value.GetString() ?? ""))
                    throw new CustomFieldValidationException($"\"{def.Label}\" must be one of: {string.Join(", ", options)}");
                break;
            default:
                throw new CustomFieldValidationException($"Unknown field type \"{def.FieldType}\" for \"{def.Label}\"");
        }
    }
}
