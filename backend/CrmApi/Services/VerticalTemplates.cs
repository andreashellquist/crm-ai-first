namespace CrmApi.Services;

// A vertical starter template is a static, in-repo bundle of settings applied
// once at workspace creation (registration, or Google OAuth first-sign-in) —
// see the workspace-customization skill §3. After provisioning, a template's
// output (Pipeline/Stage rows, WorkspaceSettings.Terminology,
// FieldDefinition rows) is ordinary editable workspace data; nothing in the
// app ever re-checks "which template did this workspace start from."
public record VerticalTemplateTerm(string? Singular = null, string? Plural = null, string? Label = null);

public record VerticalTemplateStage(string Name, int Probability, bool IsWon = false, bool IsLost = false);

public record VerticalTemplatePipeline(string Name, List<VerticalTemplateStage> Stages);

public record VerticalTemplateField(string EntityType, string Key, string Label, string FieldType, List<string>? Options = null);

public record VerticalTemplate(
    string Id,
    string Name,
    string Description,
    Dictionary<string, VerticalTemplateTerm> Terminology,
    VerticalTemplatePipeline Pipeline,
    List<VerticalTemplateField> Fields,
    List<string>? SuggestedModules = null
);

public static class VerticalTemplates
{
    // The generic default — deliberately zero terminology overrides and zero
    // custom fields, and its pipeline/stages are the same shape Data/Seed.cs
    // has always used, so picking this template is equivalent to "no
    // customization," not a fourth thing to reconcile against the seed data.
    public const string DefaultId = "saas-sales";

    public static readonly List<VerticalTemplate> All =
    [
        new VerticalTemplate(
            Id: DefaultId,
            Name: "B2B SaaS Sales",
            Description: "The default sales pipeline — deals, companies, and contacts with no relabeling.",
            Terminology: [],
            Pipeline: new VerticalTemplatePipeline("New Business",
            [
                new VerticalTemplateStage("Prospecting", 10),
                new VerticalTemplateStage("Qualified", 30),
                new VerticalTemplateStage("Proposal", 60),
                new VerticalTemplateStage("Negotiation", 80),
                new VerticalTemplateStage("Closed Won", 100, IsWon: true),
                new VerticalTemplateStage("Closed Lost", 0, IsLost: true),
            ]),
            Fields: []
        ),
        new VerticalTemplate(
            Id: "real-estate",
            Name: "Real Estate",
            Description: "Track listings from intake through close, with property details on every deal.",
            Terminology: new Dictionary<string, VerticalTemplateTerm>
            {
                ["deal"] = new(Singular: "Listing", Plural: "Listings"),
                ["company"] = new(Singular: "Property Owner", Plural: "Property Owners"),
            },
            Pipeline: new VerticalTemplatePipeline("Listings Pipeline",
            [
                new VerticalTemplateStage("New Listing", 10),
                new VerticalTemplateStage("Showing", 30),
                new VerticalTemplateStage("Offer", 50),
                new VerticalTemplateStage("Under Contract", 80),
                new VerticalTemplateStage("Sold", 100, IsWon: true),
                new VerticalTemplateStage("Withdrawn", 0, IsLost: true),
            ]),
            Fields:
            [
                new VerticalTemplateField("deal", "bedrooms", "Bedrooms", "number"),
                new VerticalTemplateField("deal", "square_footage", "Square Footage", "number"),
                new VerticalTemplateField("deal", "mls_status", "MLS Status", "select", ["Active", "Pending", "Sold"]),
            ],
            SuggestedModules: ["listings"]
        ),
        new VerticalTemplate(
            Id: "recruiting",
            Name: "Recruiting",
            Description: "Track candidates through the placement pipeline for your client companies.",
            Terminology: new Dictionary<string, VerticalTemplateTerm>
            {
                ["deal"] = new(Singular: "Placement", Plural: "Placements"),
                ["contact"] = new(Singular: "Candidate", Plural: "Candidates"),
                ["company"] = new(Singular: "Client", Plural: "Clients"),
            },
            Pipeline: new VerticalTemplatePipeline("Recruiting Pipeline",
            [
                new VerticalTemplateStage("Sourced", 10),
                new VerticalTemplateStage("Screened", 25),
                new VerticalTemplateStage("Interviewing", 50),
                new VerticalTemplateStage("Offer Extended", 75),
                new VerticalTemplateStage("Placed", 100, IsWon: true),
                new VerticalTemplateStage("Fell Through", 0, IsLost: true),
            ]),
            Fields:
            [
                new VerticalTemplateField("contact", "current_title", "Current Title", "text"),
                new VerticalTemplateField("contact", "years_experience", "Years of Experience", "number"),
                new VerticalTemplateField("deal", "role_level", "Role Level", "select", ["Junior", "Mid", "Senior", "Lead"]),
            ]
        ),
    ];

    public static VerticalTemplate? Find(string id) => All.FirstOrDefault(t => t.Id == id);
}
