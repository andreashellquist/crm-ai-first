---
name: workspace-customization
description: The concrete pattern for making this one CRM codebase work across different markets (real estate, recruiting, insurance, B2B SaaS sales, etc.) via per-workspace settings — terminology overrides, custom fields, pipeline/vertical starter templates, and optional modules. Load this whenever a request implies "this should work differently for a different kind of business," before adding a vertical-specific field, page, or branch anywhere in the codebase.
---

# Workspace customization (modular, multi-vertical CRM)

## Governing rule

**One schema, one codebase, many markets — expressed entirely as data.** There is
never an `if (workspace.vertical === "real_estate")` branch anywhere in the app.
A vertical is just a bundle of settings (terminology + custom fields + a starter
pipeline) applied to an otherwise generic workspace. See `crm-domain-expert` for
the three-tier decision process (core field vs. custom field vs. module) this
pattern implements, and `crm-data-model` for the underlying tables
(`WorkspaceSettings`, `FieldDefinition`).

**Built**: terminology overrides + custom fields (§1–2) —
`Models/WorkspaceSettings.cs`, `Models/FieldDefinition.cs`,
`Controllers/WorkspaceSettingsController.cs`,
`Controllers/FieldDefinitionsController.cs`,
`Services/TerminologyResolver.cs`, `Services/CustomFieldValidator.cs`;
vertical starter templates (§3) — `Services/VerticalTemplates.cs`,
`Services/WorkspaceProvisioningService.cs`, `POST /api/auth/register`, the
`/signup` template picker; and the first optional module, real-estate
listings (§4) — `Models/Listing.cs`, `Controllers/ListingsController.cs`,
the `/settings` module toggle, and the Listing panel on the deal detail
page. All in `backend/CrmApi` (frontend at `src/app/signup`,
`src/app/(app)/settings`, `src/app/(app)/pipeline/[dealId]`).

## 1. Terminology overrides

`WorkspaceSettings.terminology` is a `Json` map from canonical entity/field name
to a display label, e.g.:

```json
{
  "deal": { "singular": "Listing", "plural": "Listings" },
  "company": { "singular": "Property Owner", "plural": "Property Owners" },
  "stage.negotiation": { "label": "Under Contract" }
}
```

Resolve it through one shared helper used everywhere user-facing text is
produced — UI copy, email templates, AI prompt vocabulary — with a fallback to
the canonical English term when a key isn't overridden. Built as
`TerminologyResolver.Resolve(terminologyJson, key, fallback, plural: false)`:

```csharp
public static string Resolve(string? terminologyJson, string key, string fallback, bool plural = false)
{
    // deserialize terminologyJson to {key: {singular, plural, label}},
    // return (plural ? term.Plural : term.Singular) ?? term.Label ?? fallback
}
```

`DealScoringService` is the first consumer — it resolves `"deal"` into its
system prompt and tool description before calling Claude (e.g. "Score this
Listing based on these signals" for a real-estate workspace), while the tool
schema's keys (`companyName`, `stageName`, ...) stay canonical regardless, per
`ai-features-architect`'s vertical-agnostic-prompts guidance below. Never
hardcode "Deal"/"Company"/etc. directly in a controller response or prompt
string if there's any chance a workspace has relabeled it — route it through
`TerminologyResolver`. Internal identifiers (route segments, tool/field names,
database columns) are **never** affected by terminology — only display text
and generated prose are.

## 2. Custom fields

`FieldDefinition` rows (workspace + entityType scoped) describe extra fields;
values live in the `CustomFields` jsonb-as-text column on Contact/Company/Deal,
keyed by `FieldDefinition.Key`. To add a field for a workspace, insert a
`FieldDefinition` row via `POST /api/field-definitions` — no migration, no
deploy.

- **Validation**: `CustomFieldValidator.ValidateAndSerialize(definitions, input)`
  loads that workspace's `FieldDefinition` rows for the target entity type and
  validates a request's `customFields` object against them before it's ever
  persisted — rejects unknown keys, enforces `Required`, type-checks each
  value against `FieldType` (`select`'s `Options` allow-list included) — see
  `backend-api-engineer`'s validation conventions. `ContactsController.Create`
  is the one entity endpoint wired to it today; Company/Deal have the column
  and the validator is entity-type-agnostic, but neither has a create/update
  endpoint yet.
- **Forms/tables**: render generically from `FieldDefinition` (see
  `frontend-engineer`'s vertical-agnostic UI conventions) — one generic
  "custom field input" component keyed off `fieldType`, not one form per
  vertical. Not yet built on the frontend.
- **AI context**: when a custom field is relevant to a prompt (scoring,
  drafting, summarization), include it as `label: value`, not `key: value` —
  the model and any human reviewing output should see "Bedrooms: 3", not
  "bedrooms: 3" or a raw UUID-keyed blob.

## 3. Vertical starter templates

A template is a static, in-repo data structure (not a database concept) applied
once at workspace creation. Built in `Services/VerticalTemplates.cs`:

```csharp
public record VerticalTemplateTerm(string? Singular = null, string? Plural = null, string? Label = null);
public record VerticalTemplateStage(string Name, int Probability, bool IsWon = false, bool IsLost = false);
public record VerticalTemplatePipeline(string Name, List<VerticalTemplateStage> Stages);
public record VerticalTemplateField(string EntityType, string Key, string Label, string FieldType, List<string>? Options = null);

public record VerticalTemplate(
    string Id, // "saas-sales", "real-estate", "recruiting"
    string Name,
    string Description,
    Dictionary<string, VerticalTemplateTerm> Terminology,
    VerticalTemplatePipeline Pipeline,
    List<VerticalTemplateField> Fields,
    List<string>? SuggestedModules = null
);
```

Three templates ship today: `saas-sales` (`VerticalTemplates.DefaultId` — zero
terminology overrides and zero custom fields, same stage names Data/Seed.cs
has always used, so picking it is equivalent to "no customization"),
`real-estate` (Listing/Property Owner terminology, a listing-intake-to-close
pipeline, bedrooms/square-footage/MLS-status fields on the deal), and
`recruiting` (Placement/Candidate/Client terminology, a sourced-to-placed
pipeline, current-title/years-experience on the contact and role-level on the
deal).

`WorkspaceProvisioningService.ProvisionAsync(workspaceId, templateId)` applies
one: creates a `Pipeline` + its `Stage` rows, sets
`WorkspaceSettings.Terminology`, and inserts the template's `FieldDefinition`
rows, all in one call. After that, it's ordinary workspace data — editable,
deletable, no different from a workspace that configured everything by hand.
Templates are a checklist a workspace picks at signup, not a runtime concept
the app ever re-checks.

Two callers, both in `AuthController`: `POST /api/auth/register` (the real
email+password signup flow — `/signup` renders a template picker fed by the
public `GET /api/auth/templates`, lets the user pick one, and provisions it in
the same request that creates the user/workspace) and `GoogleExchange`'s
first-time-sign-in path, which has no template-picker step and defaults to
`VerticalTemplates.DefaultId`. This closed a real gap: before
`WorkspaceProvisioningService` existed, `GoogleExchange` created a bare
`Workspace` with no `Pipeline` at all, which would have broken the pipeline
board's `IsDefault` lookup on a first-time Google sign-in — see
`MultiTenantIsolationTests`-adjacent regression test
`GoogleExchange_NewUser_ProvisionsAWorkingDefaultPipeline` in
`AuthControllerTests.cs`.

Keep template definitions in one place (`Services/VerticalTemplates.cs`), one
`VerticalTemplate` per vertical, reviewed the way any other product content
is — adding a fourth vertical is adding one more record to `VerticalTemplates.All`,
not a migration or a new code path.

## 4. Optional modules

For data that genuinely doesn't fit Contact/Company/Deal (e.g. a real-estate
`Listing` with square footage and MLS status, an insurance `Policy` with a
renewal date and coverage limits): a module is a self-contained set of tables +
routes + components, gated on `WorkspaceSettings.enabledModules`.

- A module's tables follow the exact same workspaceId/index conventions as
  core tables (`database-schema-expert`) and typically FK to a core
  `Deal`/`Contact`/`Company` rather than replacing it — a `Listing` extends a
  `Deal`, it isn't a competing concept.
- Core flows (contacts, deals, pipeline, activities, tasks, AI features) must
  work correctly with **zero** modules enabled — a module adds capability, it's
  never a dependency of the core product.
- Before building a new module, check with `crm-domain-expert` whether the
  requirement is actually just a custom field in disguise — modules are for
  genuinely distinct data shapes, not an easy escape hatch from the custom-field
  tier.

**Reference implementation — `listings`**, real-estate's optional module.
`Models/Listing.cs` FKs 1:1 to `Deal` (unique index on `DealId`, cascades on
Deal delete) and holds structured fields the real-estate `VerticalTemplate`'s
custom fields deliberately don't cover (listing agent, listing URL, open
house time, commission percent) — bedrooms/square-footage/MLS-status stay
`FieldDefinition` custom fields, since those are genuinely just freeform
key/value data, not a reason to reach for a module.

- `Controllers/ListingsController.cs` (`GET`/`PUT /api/deals/{dealId}/listing`)
  checks `WorkspaceSettings.EnabledModules.Contains("listings")` before doing
  anything else — 403 if the module is off, regardless of whether the Deal
  exists, so a disabled module never leaks whether data exists behind it.
  Every query is additionally scoped by both `DealId` and `WorkspaceId`
  (`MultiTenantIsolationTests.Listing_ForAnotherWorkspacesDeal_...`).
- The real-estate `VerticalTemplate`'s `SuggestedModules: ["listings"]`
  flows straight into `WorkspaceSettings.EnabledModules` at provisioning
  (`WorkspaceProvisioningService`) — picking that template at signup turns
  the module on automatically, no separate step.
- `/settings` (`src/app/(app)/settings`) is the module on/off switch —
  before this it didn't exist at all: `WorkspaceSettingsController`'s PUT
  endpoint had been reachable only via direct API calls (tests, curl) since
  it shipped, with zero frontend. It's intentionally minimal: a checkbox per
  known module, plus a read-only terminology summary — not a general
  settings/terminology editor.
- The Listing panel on the deal detail page
  (`src/app/(app)/pipeline/[dealId]/listing-panel.tsx`) is fetched and
  rendered only when the module is enabled — absent entirely otherwise,
  which is the "zero modules enabled" requirement made concrete: nothing
  else on that page depends on `Listing` existing.

## What "modular" does not mean here

- Not a fully dynamic/EAV schema where every entity is user-defined from
  scratch (too much complexity for this team/stage — see `CLAUDE.md`'s stack
  rationale). Core entities are fixed; only the periphery (labels, extra
  fields, optional modules) is configurable.
- Not a fork or feature-branch per customer/vertical. Every workspace runs the
  same deployed code; differences are rows in `WorkspaceSettings`/
  `FieldDefinition`/`Pipeline`, never a compile-time or deploy-time choice.
