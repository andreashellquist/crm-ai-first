namespace CrmApi.Models;

// Describes one custom field for a workspace + entity type — see the
// workspace-customization skill. Values live in that entity's CustomFields
// jsonb column, keyed by FieldDefinition.Key. Adding a field is inserting a
// row here, never a migration.
public class FieldDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string WorkspaceId { get; set; }
    public required string EntityType { get; set; } // "contact" | "company" | "deal"
    public required string Key { get; set; } // stable key used in that entity's CustomFields jsonb
    public required string Label { get; set; } // human-facing label, shown in forms/tables/AI context
    public required string FieldType { get; set; } // "text" | "number" | "select" | "date" | "boolean"
    public string? Options { get; set; } // jsonb text — for "select", array of allowed values
    public bool Required { get; set; }
    public int Order { get; set; }

    public Workspace? Workspace { get; set; }
}
