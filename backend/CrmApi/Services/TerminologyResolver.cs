using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrmApi.Services;

// Resolves a canonical entity/field key to a workspace's configured display
// label, falling back to the canonical English term — see the
// workspace-customization skill. Use anywhere user-facing text is produced:
// API response labels, and AI prompt vocabulary (DealScoringService). Never
// branch app logic on the resolved value — only display/prose text goes
// through this; tool/field names stay on the canonical key.
public static class TerminologyResolver
{
    private record TermOverride(
        [property: JsonPropertyName("singular")] string? Singular,
        [property: JsonPropertyName("plural")] string? Plural,
        [property: JsonPropertyName("label")] string? Label);

    public static string Resolve(string? terminologyJson, string key, string fallback, bool plural = false)
    {
        if (string.IsNullOrWhiteSpace(terminologyJson) || terminologyJson == "{}") return fallback;

        Dictionary<string, TermOverride>? map;
        try
        {
            map = JsonSerializer.Deserialize<Dictionary<string, TermOverride>>(terminologyJson);
        }
        catch (JsonException)
        {
            // A workspace's Terminology jsonb should always round-trip through
            // WorkspaceSettingsController, but never let malformed data break
            // an unrelated request — fall back to the canonical term.
            return fallback;
        }

        if (map is null || !map.TryGetValue(key, out var term)) return fallback;
        return (plural ? term.Plural : term.Singular) ?? term.Label ?? fallback;
    }
}
