namespace CrmApi.Models;

// One row per workspace — see the workspace-customization skill. Terminology
// is a jsonb map of canonical entity/field key -> display label overrides,
// e.g. {"deal":{"singular":"Listing","plural":"Listings"}}. Never parsed for
// branching logic, only resolved for display/prompt text (TerminologyResolver).
public class WorkspaceSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string WorkspaceId { get; set; } // unique
    public string Terminology { get; set; } = "{}"; // jsonb text
    public List<string> EnabledModules { get; set; } = [];

    public Workspace? Workspace { get; set; }
}
