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
the canonical English term when a key isn't overridden:

```ts
function t(settings: WorkspaceSettings, key: string, fallback: string): string {
  return settings.terminology?.[key]?.label
      ?? settings.terminology?.[key]?.singular
      ?? fallback;
}
```

Never hardcode "Deal"/"Company"/etc. directly in a component or prompt string if
there's any chance a workspace has relabeled it — route it through `t()`.
Internal identifiers (route segments, tool/field names, database columns) are
**never** affected by terminology — only display text and generated prose are.

## 2. Custom fields

`FieldDefinition` rows (workspace + entityType scoped) describe extra fields;
values live in the existing `customFields Json` column on Contact/Company/Deal,
keyed by `FieldDefinition.key`. To add a field for a workspace, insert a
`FieldDefinition` row — no migration, no deploy.

- **Validation**: build a Zod schema at request time from that workspace's
  `FieldDefinition` rows (map `fieldType` → a Zod type, apply `required`), and
  validate `customFields` against it in the Server Action, same as any other
  input — see `backend-api-engineer`'s validation conventions.
- **Forms/tables**: render generically from `FieldDefinition` (see
  `frontend-engineer`'s vertical-agnostic UI conventions) — one generic
  "custom field input" component keyed off `fieldType`, not one form per
  vertical.
- **AI context**: when a custom field is relevant to a prompt (scoring,
  drafting, summarization), include it as `label: value`, not `key: value` —
  the model and any human reviewing output should see "Bedrooms: 3", not
  "bedrooms: 3" or a raw UUID-keyed blob.

## 3. Vertical starter templates

A template is a static, in-repo data structure (not a database concept) applied
once at workspace creation:

```ts
interface VerticalTemplate {
  id: string; // "real-estate", "recruiting", "saas-sales", ...
  name: string;
  terminology: Record<string, { singular?: string; plural?: string; label?: string }>;
  pipeline: { name: string; stages: { name: string; probability: number; isWon?: boolean; isLost?: boolean }[] };
  fields: { entityType: string; key: string; label: string; fieldType: string; options?: string[] }[];
  suggestedModules?: string[];
}
```

Applying a template means: create one `Pipeline` + its `Stage` rows, insert the
listed `FieldDefinition` rows, set `WorkspaceSettings.terminology`, and
optionally enable modules. After that, it's ordinary workspace data — editable,
deletable, no different from a workspace that configured everything by hand.
Templates are a checklist a workspace picks at signup, not a runtime concept the
app ever re-checks.

Keep template definitions in one place (e.g. `lib/vertical-templates/`), one
file per vertical, reviewed the way any other product content is.

## 4. Optional modules

For data that genuinely doesn't fit Contact/Company/Deal (e.g. a real-estate
`Listing` with square footage and MLS status, an insurance `Policy` with a
renewal date and coverage limits): a module is a self-contained set of tables +
routes + components, gated on `WorkspaceSettings.enabledModules`.

- A module's tables follow the exact same workspaceId/soft-delete/index
  conventions as core tables (`database-schema-expert`) and typically FK to a
  core `Deal`/`Contact`/`Company` rather than replacing it — a `Listing` extends
  a `Deal`, it isn't a competing concept.
- Core flows (contacts, deals, pipeline, activities, tasks, AI features) must
  work correctly with **zero** modules enabled — a module adds capability, it's
  never a dependency of the core product.
- Before building a new module, check with `crm-domain-expert` whether the
  requirement is actually just a custom field in disguise — modules are for
  genuinely distinct data shapes, not an easy escape hatch from the custom-field
  tier.

## What "modular" does not mean here

- Not a fully dynamic/EAV schema where every entity is user-defined from
  scratch (too much complexity for this team/stage — see `CLAUDE.md`'s stack
  rationale). Core entities are fixed; only the periphery (labels, extra
  fields, optional modules) is configurable.
- Not a fork or feature-branch per customer/vertical. Every workspace runs the
  same deployed code; differences are rows in `WorkspaceSettings`/
  `FieldDefinition`/`Pipeline`, never a compile-time or deploy-time choice.
