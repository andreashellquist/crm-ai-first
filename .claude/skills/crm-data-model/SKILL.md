---
name: crm-data-model
description: Canonical EF Core entity reference for this CRM's core entities (Workspace, Contact, Company, Deal, Pipeline, Stage, Activity, Task, WorkspaceMember), living in backend/CrmApi/Models. Load this before creating or modifying an entity, adding a new CRM object, or writing a migration, so new tables follow the same multi-tenancy, soft-delete, and naming conventions as everything else instead of drifting.
---

# CRM data model

Reference schema for this project's core entities, as actually implemented in
`backend/CrmApi/Models/*.cs` (one file per entity) and configured in
`backend/CrmApi/Data/AppDbContext.cs`. Treat this as the source of truth for
naming and relations; extend it rather than inventing parallel structures. See
the `crm-domain-expert` agent for *why* the model is shaped this way, and
`database-schema-expert` for indexing/RLS/migration conventions.

## Canonical EF Core shape

```csharp
// Models/Workspace.cs
public class Workspace
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string Name { get; set; }
    public string DefaultCurrency { get; set; } = "USD"; // ISO 4217; see i18n-currency-timezone skill
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<WorkspaceMember> Members { get; set; } = [];
    public List<Contact> Contacts { get; set; } = [];
    public List<Company> Companies { get; set; } = [];
    public List<Deal> Deals { get; set; } = [];
    public List<Pipeline> Pipelines { get; set; } = [];
    public List<Activity> Activities { get; set; } = [];
    public WorkspaceSettings? Settings { get; set; } // see "Workspace configuration" below
    public List<FieldDefinition> FieldDefinitions { get; set; } = [];
}

public class WorkspaceMember
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string WorkspaceId { get; set; }
    public required string UserId { get; set; }
    public string Role { get; set; } = "member"; // owner | admin | member
    public string Timezone { get; set; } = "UTC"; // IANA tz, e.g. "America/New_York" — see i18n-currency-timezone
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Workspace? Workspace { get; set; }
    public User? User { get; set; }
}

// Models/Contact.cs
public class Contact
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string WorkspaceId { get; set; }
    public string? CompanyId { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string LifecycleStage { get; set; } = "lead"; // subscriber|lead|mql|sql|opportunity|customer|churned
    public string CustomFields { get; set; } = "{}"; // jsonb: keyed by FieldDefinition.Key, see workspace-customization skill
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeletedAt { get; set; }

    public Workspace? Workspace { get; set; }
    public Company? Company { get; set; }
    public List<Deal> Deals { get; set; } = [];
    public List<Activity> Activities { get; set; } = [];
}

// Models/Company.cs
public class Company
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string WorkspaceId { get; set; }
    public required string Name { get; set; }
    public string? Domain { get; set; }
    public string CustomFields { get; set; } = "{}"; // jsonb: keyed by FieldDefinition.Key, see workspace-customization skill
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeletedAt { get; set; }

    public Workspace? Workspace { get; set; }
    public List<Contact> Contacts { get; set; } = [];
    public List<Deal> Deals { get; set; } = [];
    public List<Activity> Activities { get; set; } = [];
}

// Models/Pipeline.cs
public class Pipeline
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string WorkspaceId { get; set; }
    public required string Name { get; set; }
    public bool IsDefault { get; set; }

    public Workspace? Workspace { get; set; }
    public List<Stage> Stages { get; set; } = [];
    public List<Deal> Deals { get; set; } = [];
}

public class Stage
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string PipelineId { get; set; }
    public required string Name { get; set; }
    public int Order { get; set; }
    public int Probability { get; set; } // 0-100
    public bool IsWon { get; set; }
    public bool IsLost { get; set; }

    public Pipeline? Pipeline { get; set; }
    public List<Deal> Deals { get; set; } = [];
}

// Models/Deal.cs
public class Deal
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string WorkspaceId { get; set; }
    public required string PipelineId { get; set; }
    public required string StageId { get; set; }
    public string? CompanyId { get; set; }
    public int? AmountCents { get; set; } // int, not long — see database-schema-expert
    public string? Currency { get; set; } // ISO 4217; falls back to Workspace.DefaultCurrency when unset
    public string ForecastCategory { get; set; } = "pipeline"; // pipeline|best_case|commit|closed
    public string CustomFields { get; set; } = "{}"; // jsonb: keyed by FieldDefinition.Key, see workspace-customization skill
    public DateTime? ClosedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeletedAt { get; set; }

    // AI deal scoring (ai-features-architect / lead-deal-scoring skill)
    public int? AiScore { get; set; }
    public string? AiScoreRationale { get; set; }
    public string? AiScoreSignals { get; set; } // jsonb text
    public DateTime? AiScoredAt { get; set; }

    // AI deal summary — summarize-on-read with an incremental cache
    // (SummarizationService only sends Claude activity logged after
    // AiSummarizedAt plus the prior summary text, not the whole history each
    // time). See ai-features-architect's "Summarization" pattern.
    public string? AiSummary { get; set; }
    public DateTime? AiSummarizedAt { get; set; }

    public Workspace? Workspace { get; set; }
    public Pipeline? Pipeline { get; set; }
    public Stage? Stage { get; set; }
    public Company? Company { get; set; }
    public List<Contact> Contacts { get; set; } = [];
    public List<Activity> Activities { get; set; } = [];
    public List<TaskItem> Tasks { get; set; } = [];
}

// Models/Activity.cs
public class Activity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string WorkspaceId { get; set; }
    public required string Type { get; set; } // call | email | meeting | note
    public string? Body { get; set; }
    public string? ContactId { get; set; }
    public string? CompanyId { get; set; }
    public string? DealId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Workspace? Workspace { get; set; }
    public Contact? Contact { get; set; }
    public Company? Company { get; set; }
    public Deal? Deal { get; set; }
}

// Models/TaskItem.cs — named TaskItem, not Task, to avoid colliding with
// System.Threading.Tasks.Task. Same polymorphic-attachment shape as Activity.
public class TaskItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string WorkspaceId { get; set; }
    public required string Title { get; set; }
    public DateTime? DueAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public bool AiSuggested { get; set; }
    public string? ContactId { get; set; }
    public string? CompanyId { get; set; }
    public string? DealId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Workspace? Workspace { get; set; }
    public Contact? Contact { get; set; }
    public Company? Company { get; set; }
    public Deal? Deal { get; set; }
}
```

`AppDbContext.OnModelCreating` (see `database-schema-expert` for the delete-behavior
reasoning) configures indexes and relationships via Fluent API — e.g.:

```csharp
modelBuilder.Entity<Deal>(e =>
{
    e.HasIndex(d => new { d.WorkspaceId, d.StageId });
    e.HasIndex(d => new { d.WorkspaceId, d.PipelineId });
    e.HasOne(d => d.Workspace).WithMany(w => w.Deals)
        .HasForeignKey(d => d.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
    e.HasOne(d => d.Stage).WithMany(s => s.Deals)
        .HasForeignKey(d => d.StageId).OnDelete(DeleteBehavior.Restrict);
    e.HasMany(d => d.Contacts).WithMany(c => c.Deals)
        .UsingEntity(j => j.ToTable("DealContacts"));
});
```

## Workspace configuration

Makes the same schema work across verticals — see the `workspace-customization`
skill for the full pattern. Built as `Models/WorkspaceSettings.cs` and
`Models/FieldDefinition.cs`, exposed via `WorkspaceSettingsController` and
`FieldDefinitionsController` (both `[RequireRole("owner", "admin")]` on writes,
readable by any workspace member):

```csharp
public class WorkspaceSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string WorkspaceId { get; set; } // unique
    public string Terminology { get; set; } = "{}"; // jsonb: entity/field label overrides
    public List<string> EnabledModules { get; set; } = []; // e.g. ["listings", "policies"] — Npgsql maps List<string> to text[] natively
}

public class FieldDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string WorkspaceId { get; set; }
    public required string EntityType { get; set; } // "contact" | "company" | "deal"
    public required string Key { get; set; } // stable key used in that entity's CustomFields jsonb
    public required string Label { get; set; } // human-facing label, shown in forms/tables/AI context
    public required string FieldType { get; set; } // "text" | "number" | "select" | "date" | "boolean"
    public string? Options { get; set; } // jsonb: for "select", array of allowed values
    public bool Required { get; set; }
    public int Order { get; set; }
}
```

`Services/CustomFieldValidator.cs` validates a request's `customFields` object
against a workspace's `FieldDefinition` rows for the target entity type before
it's ever persisted — rejects unknown keys, enforces `Required`, and
type-checks each value against `FieldType` (including `select`'s `Options`
allow-list). `Services/TerminologyResolver.cs` resolves a canonical key to a
workspace's configured label (falling back to the canonical English term),
used both in API responses and — see `DealScoringService` — in AI prompt text,
per `ai-features-architect`'s vertical-agnostic-prompts guidance.

`ContactsController.Create`, `CompaniesController.Update`, and
`PipelineController.UpdateDeal` all validate `customFields` through
`CustomFieldValidator` before persisting — the validator is entity-type-agnostic,
so wiring a new endpoint to it is the same few lines each time.
```

## Rules when extending this model

- Every new tenant-scoped entity gets `WorkspaceId` plus a composite index
  pairing it with whatever's commonly filtered/sorted by — never a bare index
  on `WorkspaceId` alone.
- User-facing records get `DeletedAt` soft delete; join/log tables don't need
  it.
- Polymorphic attachment (`Activity`/`Task` → `Contact`/`Company`/`Deal`) uses
  explicit nullable FK columns, not a generic `EntityType`/`EntityId` pair.
- Money is `int` cents, never `float`/`double`.
- A new vertical-specific requirement is **not** a reason to add a column to
  `Contact`/`Company`/`Deal` — it's a `FieldDefinition` row (or, if the data
  shape is genuinely different, an optional module table). See
  `workspace-customization` for the decision process and how terminology,
  custom fields, and modules fit together end to end.
- This file covers the core transactional entities only. Reporting read-model
  tables, `Notification`/`NotificationPreference`, and `ApiKey`/
  `WebhookSubscription`/`WebhookDelivery` live in their own skills
  (`reporting-read-models`, `notifications-and-digests`,
  `public-api-and-webhooks`) rather than here, since they're owned by
  different agents and have different lifecycle/consistency requirements
  than the core model — don't merge them into this file. Those skills still
  show Prisma-era TypeScript; translate the shape, don't copy it verbatim.
