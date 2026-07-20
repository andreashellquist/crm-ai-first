# crm-ai-first

An AI-first CRM. "AI-first" means the default way a user accomplishes a task is by
delegating it to an AI agent (triage a lead, draft a follow-up, summarize a deal's
history, decide the next best action) — traditional CRUD screens exist, but they are
the fallback, not the primary interface.

## Status

Phase 0 walking skeleton is complete and its gaps have been closed, on a split
frontend/backend architecture (see "Chosen stack" below): sign in (dev-only
email+password, plus a scaffolded-but-not-yet-configured Google OAuth path —
see `auth-security-expert`), create a contact, work the pipeline board
(drag-and-drop with a keyboard/screen-reader `<select>` fallback — same
`handleMove` function underneath both), log activities and tasks against a
deal, backed by the ASP.NET Core API in `backend/CrmApi`.

Phase 1 is well underway. A Postgres-backed job queue
(`backend/CrmApi/Services/JobWorker.cs`, an in-process `BackgroundService`)
takes every LLM call off the request path — the frontend enqueues, polls job
status, and refreshes/renders on completion rather than blocking. Four AI
features are routed through it, all covered in `ai-features-architect`:

- **Deal scoring** (`DealScoringService`) — 0-100 score + rationale, single
  forced tool call, folding in recent activity text and workspace-resolved
  terminology.
- **Email drafting** (`EmailDraftingService`) — grounded in the deal's recent
  activities and primary contact; always lands in an editable compose box,
  never auto-sent (there's no send capability in this app yet at all).
- **Deal/activity summarization** (`SummarizationService`) — cached on
  `Deal.AiSummary`/`AiSummarizedAt`, updated incrementally (only new
  activities since the last summary are sent to Claude, folded into the prior
  summary text) rather than re-summarizing the whole history each time.
- **Next-best-action** (`NextBestActionService`) — a manual multi-turn
  agentic tool-use loop (read-only tools scoped by closing over
  `dealId`/`workspaceId`, capped at 6 turns) ending in a `suggest_actions`
  tool call. Read-only by design: suggestions only, never an executed action.

The `Activity` (call/email/meeting/note) and `TaskItem` entities are in, both
polymorphic over Contact/Company/Deal, with a deal detail page to log/view
activities and Company/Deal update endpoints (`CompaniesController`,
`PipelineController.UpdateDeal`) wired to `CustomFieldValidator` alongside
Contact creation.

Workspace-customization scaffolding (`WorkspaceSettings`, `FieldDefinition`)
is built per the `workspace-customization` skill — `WorkspaceSettingsController`
(terminology overrides, enabled modules) and `FieldDefinitionsController`
(per-workspace custom field definitions, scoped by entity type), with
`CustomFieldValidator` enforcing them on write and `TerminologyResolver`
resolving display/prompt text with a canonical-term fallback, consumed by
every AI feature's prompt text above.

RBAC enforcement (`RequireRoleAttribute`, `backend/CrmApi/Authorization`)
checks `CurrentUser.Role` at the point of mutation, applied to the
workspace-settings and field-definition write endpoints (owner/admin only).
Building and testing this surfaced a real bug worth knowing about: ASP.NET
Core's JWT handler remaps short claim names (including `"role"` and `"sub"`)
to legacy long-form URIs by default, which silently broke `CurrentUser.Role`/
`UserId` — fixed via `options.MapInboundClaims = false` on the JWT bearer
handler in `Program.cs`. Custom claims like `"workspaceId"` were never
affected, which is why isolation tests didn't catch it.

Baseline observability is in: OpenTelemetry tracing (ASP.NET Core + HttpClient
+ Npgsql instrumentation, console exporter in dev, OTLP if
`Observability:OtlpEndpoint` is configured), structured JSON console logs in
Production with `WorkspaceId`/`TraceId` log-scope enrichment, and Sentry error
tracking wired but dormant without a `Sentry:Dsn` — see
`devops-observability-expert`. An anonymous `GET /health` endpoint exists,
used as the e2e suite's readiness probe and generally as an uptime check.

An AI eval harness (`backend/CrmApi.Eval`) runs a small fixed set of
deal-scoring scenarios against the real Claude API for human review before
prompt/tool-schema changes — deliberately outside `dotnet test`/CI since it
costs real API calls.

**Testing is two-layered and both layers run in CI** (`.github/workflows/ci.yml`,
three jobs, all blocking — see `qa-test-engineer`):

- An xUnit suite (`backend/CrmApi.Tests`) covers the backend — integration
  tests against a real test Postgres database via `WebApplicationFactory<Program>`,
  a fake `IAnthropicMessagesClient` for deterministic AI-call tests, and
  multi-tenant isolation tests as the highest-priority category.
- A Playwright e2e suite (`e2e/`) covers the golden-path user flows — sign in,
  create a contact, move a deal through the pipeline, log an activity, and a
  reachability smoke test for each AI feature's request/poll/render loop
  (content assertions belong in the xUnit layer, not here — CI has no
  `ANTHROPIC_API_KEY`). `playwright.config.ts`'s `webServer` boots both apps
  against a seeded dev database (`dotnet run --project backend/CrmApi -- seed`,
  which now also applies migrations, making it a self-sufficient bootstrap for
  a fresh database).

Phase 2 ("Connected & informed") has started: CSV contact import with dedup
(`ContactImportService`) is in, per the `csv-import-dedupe` skill — parse and
preview a CSV synchronously (fast enough not to need the job queue), let the
user map columns (with best-effort auto-detection off common header names),
then run the actual import as a background job (`import_contacts`) so a
large file can't block the request or the UI. Matching priority: exact email
match, then company + exact name match, then company-domain match for
resolving/creating the `Company` a `Contact` attaches to; a match only fills
in currently-blank fields, never overwrites. v1-scoped to the fixed Contact
fields (email/firstName/lastName/phone/companyName/companyDomain) — custom-
field mapping is a deliberate follow-up, not a hidden gap.

In-app notifications (`Notification`/`NotificationPreference`, per the
`notifications-and-digests` skill) are also in — a bell in the app header
(`NotificationBell`, polling `/api/notifications/unread-count` every 20s)
with a panel listing recent notifications, mark-as-read, and mark-all-read.
The one wired trigger today is `ai_suggestion_ready`, fired when a
`next_best_action` job succeeds (`Job.RequestedByUserId`, set when
`PipelineController.NextBestAction` enqueues, is who gets notified — the
skill's other suggested triggers, `deal_assigned` and `task_overdue`, don't
have a real trigger yet: neither `Deal` nor `TaskItem` has an assignee/owner
column, so "who gets notified" is genuinely unresolved rather than a gap to
paper over with a guess). Email digests are schema-only
(`NotificationPreference.EmailDigest`) and not consumed by anything, since
there's no email-sending capability in this app at all yet.

Search & saved views round out Phase 2 so far: a header search box
(`GlobalSearch`, 250ms-debounced) hits `GET /api/search` for a cross-entity
lookup — Contact (name/email), Company (name/domain), and Deal (via its
Company's name, since `Deal` has no free-text title of its own) — grouped
results, capped at 5 per category. It's plain `ILIKE` substring matching, a
deliberate v1 choice over Postgres full-text search (`tsvector` + GIN index)
per `database-schema-expert`'s "don't reach for FTS until ILIKE demonstrably
can't keep up"/"don't index speculatively" — revisit if a workspace's
contact/company volume ever makes it measurably slow. The contacts list
(`ContactsController.List`) gained `q`/`lifecycleStage`/`sort` query params,
plain-HTML-form-driven so it works without JS and stays shareable/
bookmarkable per `frontend-engineer`'s URL-search-params convention; a
`SavedView` (per-user, v1-scoped to the contacts list) lets a user name and
recall a particular filter/sort combination. Contact/company detail pages
don't exist yet, so global search results for those two categories link to
the (now filterable) contacts list rather than a record page — only Deal
results have a real destination (`/pipeline/{dealId}`).

Phase 3 ("Insight & modularity") is now complete — reporting/dashboards, per
the `reporting-read-models` skill and `analytics-reporting-expert`. Three
read-model tables (`PipelineSnapshot`, `ForecastSnapshot`, `ActivityMetric`)
are refreshed by a `refresh_reports` background job triggered on the writes
that change what a report shows (`PipelineController.MoveDeal`/`UpdateDeal`/
`LogActivity`) — not on a timer, since this app has no periodic-job
scheduler — plus a manual "Refresh" button (`POST /api/reports/refresh`) for
data that predates the feature or was seeded directly and so never fired a
trigger. `GET /api/reports` serves the `/reports` dashboard (pipeline-by-
stage, forecast-by-category, activity-by-type, each with a CSV export
reading the same read model — proxied through a Next.js route handler,
`src/app/api/reports/export/route.ts`, since the browser can't call the .NET
API directly). The conversion/funnel report from `analytics-reporting-expert`'s
v1 set is deliberately not built yet: it needs a stage-transition-history
table that doesn't exist (`Deal` only stores its *current* `StageId`, not a
timestamped history of stage changes) — a real prerequisite gap, not a
hidden scope cut.

Phase 3's second sub-area, vertical starter templates, is also in: a real
self-serve `POST /api/auth/register` (email+password+name+workspace name)
now exists alongside login and Google OAuth, and it requires picking a
starter template from `Services/VerticalTemplates.cs` (`saas-sales` — the
generic default, `real-estate`, `recruiting`) rendered as a picker on
`/signup`, fed by the public `GET /api/auth/templates`. Applying a template
(`WorkspaceProvisioningService.ProvisionAsync`) creates the `Pipeline`+
`Stage` rows, sets `WorkspaceSettings.Terminology`, and inserts the
template's `FieldDefinition` rows in one call — see the
`workspace-customization` skill §3 for the shape. This also closed a real,
previously-shipping gap: `AuthController.GoogleExchange`'s first-time-sign-in
path used to create a bare `Workspace` with no `Pipeline` at all (it now
provisions the default template, since the OAuth flow has no template-picker
step of its own) — a first-time Google sign-in would otherwise have landed
on a completely broken pipeline board.

Phase 3's third sub-area, the first optional module shipped end-to-end, is
also in: real-estate `listings` (`workspace-customization` skill §4).
`Listing` (`Models/Listing.cs`) FKs 1:1 to a `Deal` and holds structured
fields the real-estate template's custom fields deliberately don't cover
(listing agent, listing URL, open house time, commission percent) — proving
the module pattern is genuinely distinct from the custom-field tier rather
than a fancier way to do the same thing. `Controllers/ListingsController.cs`
(`GET`/`PUT /api/deals/{dealId}/listing`) gates on
`WorkspaceSettings.EnabledModules` before touching anything else. Picking
the real-estate template at signup enables the module automatically (its
`SuggestedModules` flows into `EnabledModules` at provisioning); a new
`/settings` page is the manual on/off switch — the first frontend for
`WorkspaceSettingsController`'s PUT endpoint, which had existed since Phase 1
with no UI reachable from the app at all. The Listing panel on the deal
detail page renders only when the module is enabled, absent entirely
otherwise — the concrete proof that core flows don't depend on any module.

Phase 3's fourth and final sub-area, a WCAG 2.1 AA accessibility audit, is
also in — completing Phase 3. `e2e/accessibility.spec.ts` runs
`@axe-core/playwright` against every core page (login, signup, pipeline
board, deal detail, contacts list, contacts import, reports, settings) as a
standing regression guard, not a one-time pass — see `frontend-engineer`'s
"Accessibility & consistency" section for the two real, recurring issues the
audit found and fixed (insufficient color contrast on the app's muted/
empty-state text color, and a CSV-import file input whose `<label>` wasn't
programmatically associated via `htmlFor`/`id`). Automated scanning covers
the mechanically-detectable subset of WCAG (labels, contrast, roles,
landmarks) — it is not a substitute for manual keyboard/screen-reader
testing of new interactive patterns going forward.

Phase 4 ("Enterprise & platform") has started with the public API and
outbound webhooks, per the `public-api-and-webhooks` skill and
`api-platform-expert`. Workspace-scoped `ApiKey`s (`crm_live_`-prefixed,
SHA-256-hashed at rest, shown once at creation) authenticate a second,
`X-Api-Key`-based ASP.NET Core auth scheme (`ApiKeyAuthenticationHandler`)
that resolves to the same `workspaceId` claim session auth does, so every
`/api/v1/*` read endpoint (`contacts`, `companies`, `deals`,
`field-definitions`) re-scopes by workspace exactly like the rest of the
app — external auth is a different front door, never a parallel
less-checked path. Scopes (`{entity}:read`) gate access via
`RequireScopeAttribute`, and a fixed-window `RateLimiter` policy throttles
each key independently of the app's own internal usage. Outbound
`WebhookSubscription`s fire on a small, fixed business-event catalog
(`deal.won`, `deal.lost`, `deal.stage_changed`, `contact.created`) via the
existing `deliver_webhook` background job, which deliberately reuses
`JobWorker`'s own exponential-backoff retry rather than a bespoke one;
deliveries are HMAC-signed (`X-Crm-Signature`) and their history is visible
per-subscription so a broken integration is discoverable, not silently
dropping events. `/settings` gained API-key and webhook-management sections
— v1 is deliberately read-only and capped at 100 results per list call;
write scopes and real cursor pagination are a follow-up once a concrete
integration needs them, not a hidden gap.

Phase 4's second sub-area, SCIM user provisioning, is also in —
`Controllers/ScimUsersController.cs` implements RFC 7644's core `/Users`
resource (`api/scim/v2/Users`: list with `userName eq` filtering, get,
create, replace, patch, delete, spec-shaped JSON including the
capitalized-against-the-grain `Resources`/`Operations` attributes) for
IdP-driven provisioning, deliberately authenticated by the same `ApiKey`
infrastructure as the public API rather than a parallel secret system — a
key carrying the new `scim:users` scope is what an identity provider's SCIM
connector is configured with. Deprovisioning is the operation real IdPs
actually send (`PATCH {"op":"replace","path":"active","value":false}`),
which sets the new `WorkspaceMember.IsActive`, checked by
`AuthController.Login` on every login attempt. **Real, stated limitation**:
this blocks new logins immediately but can't invalidate a JWT already
issued before deactivation (this app has no server-side session store to
revoke against — see `auth-security-expert`'s "Enterprise auth" section);
that session stays valid until its normal 7-day expiry.

Phase 4's third sub-area, custom roles/permissions, is also in —
`Models/Role.cs`, `Authorization/Permissions.cs`,
`Authorization/RequirePermissionAttribute.cs`, `Controllers/RolesController.cs`,
`Controllers/MembersController.cs`. The `owner`/`admin`/`member` system
roles are deliberately *not* database rows — a fixed permission map in code
(`Permissions.SystemRoleHas`), so every already-provisioned workspace's
behavior is unchanged. `Role` rows exist only for workspace-created custom
roles, each an explicit subset of a fixed permission catalog
(`settings:manage`, `fields:manage`, `api_keys:manage`, `webhooks:manage`,
`members:manage`, `roles:manage`); only `owner` carries `roles:manage` by
default, so an admin (or any custom role) can never grant itself more
access than an owner already allowed. `MembersController` (new — a
workspace previously had no way to list its own members, change a role, or
remove someone at all, outside of SCIM) is what makes a role assignable,
and guards against removing/demoting a workspace's only remaining owner.
`/settings` gained Members and Roles sections. **v1 scope, deliberate**:
only `FieldDefinitionsController`'s writes are retrofitted from
`RequireRoleAttribute` to the new permission check, as the one
proof-it's-real integration point verified against full existing test
coverage; `WorkspaceSettingsController`, `ApiKeysController`, and
`WebhookSubscriptionsController` still use the original role check —
migrating them is a mechanical follow-up, not a design gap.

Phase 4's fourth sub-area, locale-aware currency/timezone formatting, is also
in, per the `i18n-currency-timezone` skill. `Workspace.DefaultCurrency` and
`WorkspaceMember.Timezone` both pre-existed in the schema since early
phases but were dead columns nothing ever read or wrote — this closed that
gap rather than adding new schema. `WorkspaceSettingsController`'s
`PUT /api/workspace/settings` now accepts and ISO-4217-validates
`defaultCurrency`; every DTO that carries a deal amount
(`PipelineBoardDto`, `ReportsResponse`) carries the workspace's
`DefaultCurrency` alongside it, and `src/lib/money.ts`'s `formatAmount` was
changed to require an explicit `fallbackCurrency` argument rather than
hardcoding `"USD"` internally — a real, live bug this closed: the pipeline
board's stage totals and every currency figure on `/reports` were
hardcoding `"USD"` regardless of a workspace's actual configured currency. A
new `MeController` (`GET`/`PUT /api/me`) is a workspace member's
self-service profile (name + IANA timezone, validated via
`TimeZoneInfo.FindSystemTimeZoneById`); `ReportsController.Get()`'s
`FormatInCallerTimezoneAsync` is the reference implementation of converting
a UTC-stored timestamp to the caller's own timezone *on the backend* and
shipping a pre-formatted string (`ReportsResponse.RefreshedAtDisplay`)
rather than a raw timestamp for the frontend to call
`.toLocaleString()` on (which silently uses the browser's timezone, not the
member's configured one). `/settings` gained "My profile" and "Default
currency" sections. **Deliberately not done**: the small number of
pre-existing `new Date(...).toLocaleString()` call sites elsewhere in the
frontend (activity timestamps, webhook delivery times) were not migrated to
the backend-formatted-string pattern in this pass — a mechanical follow-up,
not a design gap — and there's no per-user locale field yet (only
`Timezone`), so number/date *layout* (not just currency/timezone
*correctness*) still hardcodes `en-US`-shaped formatting.

Phase 4's fifth sub-area, SSO (SAML/OIDC) — the last piece of the
"Enterprise" half — is also in, built as a generic per-workspace OIDC
connector rather than SAML (most enterprise IdPs — Okta, Azure AD, Google
Workspace — support OIDC; picking one protocol well beat a half-built pair).
`Models/SsoConnection.cs`, `Services/IOidcClient.cs`/`OidcClient.cs`,
`Controllers/SsoController.cs`. Same **SCAFFOLD ONLY** treatment as
`GoogleOAuthClient`: the full discovery → authorization-code exchange →
JIT-provisioning flow is real, reviewable, and covered end-to-end against a
fake OIDC client (`FakeOidcClient`, mirroring `FakeGoogleOAuthClient`) in
the xUnit suite, but unusable in practice until a workspace admin points it
at a real IdP — this agent has no real IdP to register a client against,
identical to the Google OAuth blocker. A connection is scoped to one
workspace (`GET`/`PUT`/`DELETE /api/workspace/sso`, owner/admin to write)
and keyed by an `EmailDomain` (globally unique across workspaces) that
routes an unauthenticated sign-in attempt — entered on the new `/sso`
page — to the right IdP before the user has proven who they are.
First-time SSO sign-in is **just-in-time provisioning into the connection's
existing workspace**, deliberately not a fresh-workspace flow like
`GoogleExchange`: the workspace already exists (an admin configured the
connection on it), so there's no template-picker step and no reason to spin
up a new one. `SsoConnection.Enforced` lets an admin require SSO for a
matching domain — checked in `AuthController.Login` before the password
hash step, returning 403 (not 401, so the frontend can show "use SSO
instead" rather than a generic bad-credentials message) — off by default
per `auth-security-expert`'s "admin can enforce it, never silently disable
other login methods" guidance. `/settings` gained a "Single sign-on"
section. **Real, deliberate scope limits**: no JWKS/ID-token signature
verification (trusts the token/userinfo endpoints over a direct
server-to-server TLS connection instead, same simplification
`GoogleOAuthClient` already makes); `SsoConnection.ClientSecret` is
plaintext at rest, matching this codebase's pre-existing
`WebhookSubscription.Secret` precedent rather than a bespoke encryption
scheme — both are a real, tracked follow-up (`auth-security-expert`'s
"encrypt third-party credentials at rest," not yet implemented anywhere in
this app); and there's no e2e test exercising the actual IdP redirect for
the same reason there isn't one for Google — no real IdP is reachable from
this sandbox.

Phase 4's sixth sub-area, a SOC 2 readiness pass (`docs/SOC2_READINESS.md`),
is also in — a gap analysis against SOC 2's Trust Service Criteria (Security/
Availability/Confidentiality), not a certification and not legal/audit
advice. It surfaced and closed one real, concrete gap:
`docs/PRODUCT_SCOPE.md` §4 had long claimed "audit logging is in the schema
from Phase 0," but no such table existed anywhere in the codebase — a real
example of a docs/reality drift this pass caught rather than perpetuated.
`Models/AuditLog.cs` + `Services/AuditLogService.cs` (a small fixed action
catalog, same "fixed catalog, not everything" discipline as
`WebhookSubscription.EventTypes`) now records who changed what
security-relevant configuration and when — member role changes/removal,
role create/update/delete, API key create/revoke, SSO connection changes,
workspace settings updates — written synchronously in the same request/
transaction as the change itself (never queued, so a job-queue outage can't
silently drop trail entries), viewable read-only at `GET /api/audit-log`
(owner/admin only, capped at 100 most recent — same v1 pagination choice as
the public API) and a new "Audit log" section on `/settings`. Removing the
acting user's `User` row sets `AuditLog.ActorUserId` to null rather than
cascading the entry away (`DeleteBehavior.SetNull` in `AppDbContext.cs`) —
an audit trail that can be deleted by deleting its actor isn't one. The
readiness doc itself catalogs what else is real (multi-tenant isolation and
RBAC — the hardest-to-retrofit pieces — have been correct since Phase 0) vs.
what's a genuine, still-open gap: no dependency/vulnerability scanning in CI,
no session/JWT revocation mechanism, `SsoConnection.ClientSecret`/
`WebhookSubscription.Secret` still stored in plaintext, and — its main
conclusion — that most of what's left is organizational/process work
(incident-response runbook, vendor/subprocessor management, a periodic
access-review cadence) that cannot be shipped in a pull request, not an
engineering backlog.

Phase 4's seventh sub-area, regional privacy law/data residency and
localized billing/tax, closes out Phase 4 with two analysis documents —
`docs/REGIONAL_COMPLIANCE.md` and `docs/LOCALIZED_BILLING.md` — rather than
code, per the `regional-compliance-expert` agent's charter ("identifies
requirements and where engineering guardrails must sit — it does not
implement them, and its output is not legal advice"). The residency doc
separates three requirements often conflated in a sales conversation (legal
data-localization, contractual/procurement residency, and jurisdiction-of-
access sovereignty concerns), characterizes divergence from the GDPR/CCPA
baseline this app already assumes across UK GDPR/PIPEDA/LGPD/Singapore
PDPA/Australia Privacy Act/PIPL (PIPL is the one regime here with a real
hard divergence — mandatory in-country data localization), and names the
cheap-now guardrail (an eventual `Workspace.DataRegion` column, added
opportunistically rather than on its own migration) vs. what's genuinely
deferred (actual multi-region infrastructure). It also surfaced two real
gaps worth tracking even though fixing them is out of this document's own
scope: **no data-subject export or deletion endpoint exists anywhere** —
`Contact.DeletedAt`/`Company.DeletedAt`/`Deal.DeletedAt` are dead columns,
schema-only since nothing writes them outside `ContactsController.List`'s
read filter, the same "field exists, nothing populates it" pattern already
seen with `Workspace.DefaultCurrency` pre-i18n-work — and **every AI feature
sends real contact/deal/activity PII to Anthropic with no region control**
(`Services/AnthropicMessagesClient.cs` takes only an API key, no
region/endpoint config), which is the concrete mechanism by which workspace
PII crosses borders independent of where Postgres itself lives. The billing
doc is explicit that it's "doubly blocked" — Phase 2's billing system
(Stripe subscriptions) has never been built at all, so VAT/GST display and
e-invoicing (Italy's SDI, the EU's evolving ViDA rules, Mexico's CFDI,
India's GST e-invoicing) are requirements captured now for whoever builds
Phase 2 billing later, not a spec to implement today; it does name which
fields are cheap to add to that future billing model on day one
(`BillingCountry`, `VatNumber`/`TaxId` plus a separate validated flag,
`BillingLegalName`/`BillingAddress`, `TaxExempt`) vs. genuinely deferred
(actual VAT-rate calculation, VIES verification, per-country e-invoicing
integrations — all jobs for a tax/invoicing platform to own, not to
hand-roll). **This completes Phase 4** — every row in `docs/PRODUCT_SCOPE.md`
§3's Phase 4 group now has either working code or, where the item is
explicitly non-code (legal/tax characterization), a grounded analysis
document.

Everything else in `docs/PRODUCT_SCOPE.md` — functional scope, non-functional
bar, phased roadmap, and the explicit assumptions made to resolve an
intentionally vague brief — is still ahead. Read it before starting a new
feature area; it says what phase the feature belongs to and which expert
agent in `.claude/agents/` owns it. Notably not yet built: real OAuth
credentials (the Google flow is fully wired end to end but
`GoogleOAuth:ClientId`/`ClientSecret` ship blank — see `auth-security-expert`),
any actual email/calendar send capability, RAG over CRM history, task/deal
assignment (needed before `task_overdue`/`deal_assigned` notifications can
exist), contact/company detail pages, deal stage-transition history (needed
before a conversion/funnel report can exist), data-subject export/delete
endpoints (see `docs/REGIONAL_COMPLIANCE.md` §4 — `auth-security-expert`'s
remit), and the rest of Phase 2 (email/calendar sync with consent/
suppression gating, billing — see `docs/LOCALIZED_BILLING.md` for what that
future billing build needs to get right on tax from day one). **Phase 4 is
now complete** — see the two sub-areas above.

**Docs-consistency note**: this project moved from an all-TypeScript (Next.js +
Prisma) stack to a split Next.js frontend / .NET backend (see "Why the split"
below) after Phase 1 had already started. `CLAUDE.md`, `crm-data-model`,
`backend-api-engineer`, `database-schema-expert`, `auth-security-expert`,
`devops-observability-expert`, `qa-test-engineer`, `ai-features-architect`,
`workspace-customization`, `csv-import-dedupe`, `notifications-and-digests`,
`reporting-read-models`, `public-api-and-webhooks`, and
`i18n-currency-timezone` have been updated for the new stack. Skills further
from the migration's blast radius (`pipeline-kanban-board`,
`communication-consent-and-suppression`) still show
Prisma/TypeScript-flavored schema snippets and code examples — the *patterns
and conventions* in them (multi-tenancy, soft deletes, tool-calling
discipline, etc.) still apply, but any literal code needs translating to EF
Core/C#. Update a skill's code the next time you touch the feature area it
covers, rather than treating this as a blocking backlog item.

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
- **Testing**: xUnit for the .NET backend (`backend/CrmApi.Tests` — integration
  tests via `WebApplicationFactory<Program>` against a real test Postgres
  database, a fake `IAnthropicMessagesClient` for AI-call tests, multi-tenant
  isolation as the highest-priority category), Playwright for e2e against the
  frontend (`e2e/` — golden-path flows plus AI-feature reachability smoke
  tests; see `qa-test-engineer`). CI (`.github/workflows/ci.yml`) runs both,
  plus frontend lint/typecheck/build, on every push/PR — three jobs, all
  blocking.
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
