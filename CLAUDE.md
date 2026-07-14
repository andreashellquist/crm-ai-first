# crm-ai-first

An AI-first CRM. "AI-first" means the default way a user accomplishes a task is by
delegating it to an AI agent (triage a lead, draft a follow-up, summarize a deal's
history, decide the next best action) — traditional CRUD screens exist, but they are
the fallback, not the primary interface.

## Status

This repo currently contains no application code. What's checked in so far is the
stack decision (this file), the full product scope (`docs/PRODUCT_SCOPE.md` —
functional scope, non-functional bar, phased roadmap, and explicit assumptions
made to resolve an intentionally vague brief), and a set of Claude Code experts
(`.claude/agents/`, `.claude/skills/`) meant to keep implementation consistent
once building starts. Read `docs/PRODUCT_SCOPE.md` before starting a new feature
area — it says what phase it belongs to and which expert owns it.

## Chosen stack

Picked for a small team shipping an AI-native SaaS quickly, with strong TypeScript
end-to-end and first-class support for the Anthropic API. Revisit if requirements
turn out to need something these don't fit.

- **Framework**: Next.js (App Router) + TypeScript, deployed on Vercel
- **UI**: Tailwind CSS + shadcn/ui, React Server Components by default, client
  components only where interactivity requires it
- **Data**: PostgreSQL via Prisma ORM; multi-tenant via a `workspace_id` column on
  every tenant-scoped table (not schema-per-tenant)
- **Auth**: Auth.js (NextAuth) — email/OAuth login, session-based, RBAC via a
  `role` on the workspace-membership join table
- **AI**: Anthropic Claude via the TypeScript SDK / Claude Agent SDK for agentic
  flows; tool use for structured actions (create task, update deal stage, send
  email draft) rather than free-text side effects
- **Background work**: queue-backed jobs (e.g. Inngest or a Postgres-backed queue)
  for anything that calls an LLM or a third-party API, so request handlers stay fast
- **Testing**: Vitest for unit/integration, Playwright for e2e
- **Validation**: Zod schemas shared between server actions/API routes and forms
- **Observability**: structured logging + tracing (e.g. OpenTelemetry) and an
  error tracker (e.g. Sentry) from Phase 0 — not deferred to "when we're bigger,"
  since retrofitting tracing into background jobs and AI calls later is far more
  work than instrumenting them as they're built (`devops-observability-expert`).

## Core domain entities

`Workspace` (tenant) → `Contact`, `Company`/`Account`, `Deal`/`Opportunity`
(belongs to a `Pipeline` + `Stage`), `Activity` (call/email/meeting/note, polymorphic
over Contact/Company/Deal), `Task`. See the `crm-domain-expert` agent and
`crm-data-model` skill before adding or changing entities — keep the model
consistent with those conventions rather than inventing parallel structures.

## Modular by design — one schema, many markets

This CRM must work for different verticals (e.g. real estate, recruiting,
insurance, B2B SaaS sales) via per-workspace **settings**, not via forked code
paths or vertical-specific schemas. Concretely:

- **Terminology is configurable.** A workspace can relabel "Deal" as "Listing"
  or "Company" as "Property Owner" without any schema or code change — labels
  are resolved at render/prompt-build time from workspace settings, never
  hardcoded as user-facing strings.
- **Fields are extensible per workspace** via a `FieldDefinition` metadata table
  + `customFields` JSON, not by adding vertical-specific columns to core tables.
- **Pipelines are just data.** Vertical "starter kits" (pipeline/stage
  templates, suggested custom fields, suggested terminology) are seeded at
  workspace creation and become ordinary workspace data afterward — the app
  has no runtime branching on "what vertical is this."
- **Optional modules**, not vertical if/else. A capability that doesn't fit the
  core model (e.g. property listings, insurance policies) ships as an optional
  module with its own tables, toggled per workspace, never required by core
  flows.

See the `workspace-customization` skill for the concrete data shapes, and
`crm-domain-expert` for when something belongs as a core field vs. a custom
field vs. a module.

## Working conventions

- Prefer Server Actions over API routes for internal mutations; use API routes only
  for webhooks (email/calendar providers, Stripe) and anything external callers hit.
- Every LLM call that can trigger a side effect must go through an explicit tool
  definition with a Zod-validated input schema — never let the model free-form a
  DB write.
- PII (emails, phone numbers, deal values) is workspace-scoped; never log full PII,
  and redact it in any prompt sent to a third-party eval/analytics service.
- New features that touch the schema, AI prompts/tools, auth, or integrations should
  consult the matching expert agent in `.claude/agents/` before implementation.
