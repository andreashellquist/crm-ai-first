# crm-ai-first

An AI-first CRM. "AI-first" means the default way a user accomplishes a task is by
delegating it to an AI agent (triage a lead, draft a follow-up, summarize a deal's
history, decide the next best action) — traditional CRUD screens exist, but they are
the fallback, not the primary interface.

## Status

Phase 0 walking skeleton is in place, now on a split frontend/backend architecture
(see "Chosen stack" below): sign in, create a contact, work the pipeline board, log
activities against a deal, backed by the ASP.NET Core API in `backend/CrmApi`. Auth
is a dev-only email+password login for now — see `auth-security-expert` before
adding real OAuth/enterprise auth.

Phase 1 has started: a Postgres-backed job queue (`backend/CrmApi/Services/JobWorker.cs`,
runs as an in-process `BackgroundService`) takes AI calls off the request path, and
deal scoring (`backend/CrmApi/Services/DealScoringService.cs`) is the first feature
routed through it — the frontend enqueues, polls job status, and refreshes on
completion rather than blocking. The `Activity` entity (call/email/meeting/note) is
in, with a deal detail page to log and view them — deal scoring folds recent
activity text into its signals.

Everything else in `docs/PRODUCT_SCOPE.md` — functional scope, non-functional bar,
phased roadmap, and the explicit assumptions made to resolve an intentionally vague
brief — is still ahead. Read it before starting a new feature area; it says what
phase the feature belongs to and which expert agent in `.claude/agents/` owns it.
Notably not yet built: Task entity, drag-and-drop on the pipeline board,
drafting/summarization/next-best-action, an AI eval harness, real OAuth, and CI.

**Docs-consistency note**: this project moved from an all-TypeScript (Next.js +
Prisma) stack to a split Next.js frontend / .NET backend (see "Why the split"
below) after Phase 1 had already started. `CLAUDE.md`, `crm-data-model`,
`backend-api-engineer`, `database-schema-expert`, and `auth-security-expert` have
been updated for the new stack. Skills further from the migration's blast radius
(`workspace-customization`, `csv-import-dedupe`, `pipeline-kanban-board`,
`observability-and-slo`, `reporting-read-models`, `public-api-and-webhooks`,
`notifications-and-digests`, `i18n-currency-timezone`,
`communication-consent-and-suppression`) still show Prisma/TypeScript-flavored
schema snippets and code examples — the *patterns and conventions* in them
(multi-tenancy, soft deletes, tool-calling discipline, etc.) still apply, but any
literal code needs translating to EF Core/C#. Update a skill's code the next time
you touch the feature area it covers, rather than treating this as a blocking
backlog item.

## Chosen stack

Picked for a small team shipping an AI-native SaaS quickly. Revisit if
requirements turn out to need something these don't fit.

- **Frontend**: Next.js (App Router) + TypeScript, deployed on Vercel — a thin
  frontend/BFF. It renders UI and forwards requests to the backend
  (server-to-server; the browser never talks to the .NET API directly, so there's
  no CORS surface and the session JWT never reaches client JS). No direct database
  access from the frontend.
- **Backend**: ASP.NET Core Web API (`backend/CrmApi`, .NET 10) — owns the domain
  model, business logic, auth, the Claude integration, and the job queue.
  Controllers, not minimal APIs, for consistency with typical enterprise .NET
  conventions (`backend-api-engineer`).
- **UI**: Tailwind CSS + custom components in the style of shadcn/ui, React Server
  Components by default, client components only where interactivity requires it.
- **Data**: PostgreSQL via EF Core (Npgsql provider); multi-tenant via a
  `WorkspaceId` column on every tenant-scoped table (not schema-per-tenant). See
  `database-schema-expert` and the `crm-data-model` skill.
- **Auth**: the API issues a JWT on login (`AuthController`); the frontend holds it
  in an HttpOnly cookie and attaches it as a Bearer token on every backend call.
  RBAC via a `Role` on the workspace-membership join table. See
  `auth-security-expert` — this replaced an earlier NextAuth v5 *beta* dependency
  specifically because running a beta major version of the credential-handling
  library was a real, avoidable risk.
- **Frontend/backend contract**: contract-first OpenAPI. The API is the source of
  truth (`backend/openapi.json`, regenerated from the running API); the frontend
  generates TypeScript types and a typed client from it (`pnpm gen:api-types` →
  `src/lib/api/schema.d.ts`, consumed via `openapi-fetch`). Neither side hand-writes
  the other's models, so they can't drift — regenerate the spec and types whenever
  the API contract changes, and treat a stale generated file as a bug.
- **AI**: Anthropic Claude via the official C# SDK (NuGet package `Anthropic` —
  **not** `Anthropic.SDK`, a different, third-party package); tool use for
  structured actions (update deal stage, score a deal, send an email draft) rather
  than free-text side effects. See the `claude-api` skill before writing any
  Claude API code — SDK bindings must come from its bundled reference or an
  official source, never guessed from another language's SDK shape.
- **Background work**: a Postgres-backed job queue (`Jobs` table, claimed via a
  single atomic `UPDATE ... FOR UPDATE SKIP LOCKED` statement) for anything that
  calls an LLM or a third-party API, so request handlers stay fast. Runs as an
  in-process `BackgroundService` for local dev / a dedicated deployment; note that
  this persistent-loop shape does not fit a serverless deployment target for the
  *API* itself (unlikely here, since ASP.NET Core is typically deployed as a
  long-running process, but worth remembering if that ever changes).
- **Testing**: xUnit for the .NET backend (not yet set up — the TypeScript Vitest
  suite that covered deal scoring was removed with the code it tested and needs a
  .NET equivalent), Playwright for e2e against the frontend.
- **Validation**: model validation in ASP.NET Core controllers (`ModelState`,
  manual checks) rather than a shared client/server schema library — the frontend
  no longer duplicates validation logic; it forwards requests and surfaces the
  API's error.
- **Observability**: structured logging + tracing (e.g. OpenTelemetry) and an
  error tracker (e.g. Sentry) from Phase 0 — not deferred to "when we're bigger,"
  since retrofitting tracing into background jobs and AI calls later is far more
  work than instrumenting them as they're built (`devops-observability-expert`).

### Why the split (from an original all-TypeScript design)

The original walking skeleton was Next.js + Prisma end to end. It moved to a
split Next.js/.NET architecture because the person building this is a senior .NET
developer — matching the stack to who's actually writing the code was judged more
valuable than staying single-language, given real, concrete factors: ASP.NET
Core's throughput/latency edge for CPU-bound work (background jobs, future
reporting rollups), a slower-moving and more stable framework ecosystem (the
original stack hit several breaking changes — Prisma 7's driver-adapter rewrite,
Next.js 16, a NextAuth v5 beta — while just building the walking skeleton), and
.NET's more mature security tooling/track record. The trade-off accepted: two
codebases, two deployments, and a contract (OpenAPI) to keep in sync instead of
one language throughout — mitigated by generating both sides' types from that one
contract rather than hand-syncing them.

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
  + a JSON custom-fields column, not by adding vertical-specific columns to core
  tables.
- **Pipelines are just data.** Vertical "starter kits" (pipeline/stage
  templates, suggested custom fields, suggested terminology) are seeded at
  workspace creation and become ordinary workspace data afterward — the app
  has no runtime branching on "what vertical is this."
- **Optional modules**, not vertical if/else. A capability that doesn't fit the
  core model (e.g. property listings, insurance policies) ships as an optional
  module with its own tables, toggled per workspace, never required by core
  flows.

See the `workspace-customization` skill for the concrete data shapes (Prisma
syntax there, translate to EF Core per the docs-consistency note above), and
`crm-domain-expert` for when something belongs as a core field vs. a custom
field vs. a module.

## Working conventions

- Frontend Server Actions exist only to forward requests to the .NET API and
  surface its errors — they hold no business logic and do not talk to the
  database. All real logic, validation, and persistence lives in `backend/CrmApi`.
- Every LLM call that can trigger a side effect must go through an explicit tool
  definition with a validated input schema — never let the model free-form a
  DB write.
- PII (emails, phone numbers, deal values) is workspace-scoped; never log full PII,
  and redact it in any prompt sent to a third-party eval/analytics service.
- New features that touch the schema, AI prompts/tools, auth, or integrations should
  consult the matching expert agent in `.claude/agents/` before implementation.
- When the API contract changes (a controller's request/response shape, a new
  endpoint), regenerate `backend/openapi.json` and run `pnpm gen:api-types` in the
  frontend before relying on the new shape in TypeScript.
