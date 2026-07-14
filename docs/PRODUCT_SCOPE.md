# Product scope: CRM AI-First (professional grade)

This document turns the vague brief ("AI-first CRM, modular across markets") into
a concrete scope: what ships, in what order, to what bar, and which explicit
assumptions fill the gaps the brief left open. It is the reference for prioritizing
work and for deciding what a new feature request actually is (core vs. custom field
vs. module — see `crm-domain-expert` — or in-scope-now vs. later phase).

Treat this as a living document: update it when a scope decision changes rather
than letting the code silently drift from it.

## 1. Assumptions (flag these — correct me if wrong)

The brief didn't specify these; picked defaults that fit "small team, ship an
AI-native SaaS quickly, modular by market":

- **Buyer**: B2B, sales-led (a company selling to other companies), not
  consumer/B2C. Verticals in scope are B2B-shaped (SaaS sales, recruiting/staffing,
  real estate brokerage, insurance agencies, agencies/professional services) —
  not, e.g., B2C loyalty/marketing CRM.
- **Customer size**: SMB to mid-market (5-500 seats per workspace) as the near-term
  target; enterprise-only features (SSO/SCIM, custom roles, dedicated
  infrastructure) are in scope but sequenced late, not blocking v1.
- **Geography/market** reading of "different type of markets": both (a) industry
  verticals — addressed by the workspace-customization system already built —
  and (b) geographic markets, which additionally implies multi-currency,
  timezone-correct scheduling, and a UI architecture ready for translation (full
  translated UI is not committed to v1; the architecture must not block it).
- **Platform**: responsive web only for v1. No native mobile app; a PWA-friendly
  responsive layout is good enough for reps checking the CRM on a phone.
- **Deployment model**: single multi-tenant SaaS deployment (Vercel + managed
  Postgres), not on-prem/self-hosted or single-tenant-per-customer. Revisit only
  if an enterprise deal specifically requires data residency this can't satisfy.

If any of these are wrong, say so — they drive real architecture decisions below
(especially multi-currency and enterprise auth timing).

## 2. Personas

- **Rep** — primary daily user. Wants the AI to remove data entry and tell them
  what to do next; falls back to manual CRUD only when AI output is wrong.
- **Manager** — owns a pipeline/team. Wants forecast accuracy, pipeline health
  visibility, and the ability to see what the AI is doing on their team's behalf
  (auditability matters to this persona specifically).
- **Workspace admin/owner** — configures the workspace: pipelines, custom fields,
  terminology, integrations, billing, roles. Usually also a manager or founder in
  smaller workspaces.
- **(Later) IT/security buyer** — enterprise persona that shows up once deals
  require SSO, SCIM, audit log export, or a security questionnaire. Not the
  primary persona for v1 but the reason Phase 4 exists.

## 3. Functional scope by area

Each area names the owning expert agent/skill (existing or newly added) and its
target phase (§6).

| Area | Scope | Owner | Phase |
|---|---|---|---|
| Core entities & pipeline | Contact/Company/Deal/Pipeline/Stage/Activity/Task, Kanban board, forecasting | `crm-domain-expert`, `database-schema-expert`, `frontend-engineer`, `pipeline-kanban-board` | 0 |
| Auth & basic permissions | Email/OAuth login, workspace membership, owner/admin/member roles | `auth-security-expert` | 0 |
| AI drafting & summarization | Email drafts, call/meeting summaries, tool-calling framework | `ai-features-architect`, `ai-tool-calling-pattern` | 1 |
| Lead/deal scoring & next-best-action | Structured, explainable scoring; suggested-action cards | `ai-features-architect`, `lead-deal-scoring` | 1 |
| Data import & cleanliness | CSV import, dedup, ongoing merge tooling | `backend-api-engineer`, `csv-import-dedupe` | 1 |
| Email & calendar integration | Gmail/Outlook OAuth sync, meeting capture into Activities | `integrations-engineer` | 2 |
| Communication consent & suppression | Per-contact/channel consent tracking, unsubscribe handling, gate on every send path (AI or manual) | **new** `regional-compliance-expert`, **new** `communication-consent-and-suppression` | 2 — ships with email sending, blocking (not deferrable to Phase 4) |
| Notifications | In-app + email digest, per-user preferences | `backend-api-engineer`, `frontend-engineer`, **new** `notifications-and-digests` | 2 |
| Search & saved views | Cross-entity search, filters, shareable saved views | `database-schema-expert`, `frontend-engineer` | 2 |
| Billing & plans | Stripe subscriptions, usage-based limits/metering | `integrations-engineer` | 2 |
| Reporting & dashboards | Pipeline/forecast reports, activity reports, exportable | **new** `analytics-reporting-expert`, **new** `reporting-read-models` | 3 |
| Vertical modularity | Terminology, custom fields, starter templates, optional modules | `crm-domain-expert`, `workspace-customization` | 3 (system already built in 0-1) |
| Observability & reliability | Logging, tracing, error tracking, alerting, SLOs, backup/DR | **new** `devops-observability-expert`, **new** `observability-and-slo` | 0 baseline, 3 maturity |
| Public API & outbound webhooks | API keys, scoped access, rate limits, customer-facing webhooks | **new** `api-platform-expert`, **new** `public-api-and-webhooks` | 4 |
| Enterprise auth | SSO (SAML/OIDC), SCIM provisioning, custom roles/permissions | `auth-security-expert` (extended) | 4 |
| Regional privacy law & data residency | Characterize per-region requirements beyond the GDPR/CCPA baseline; flag data-localization infra needs early | **new** `regional-compliance-expert` | 4 (flag early if a specific deal requires it sooner) |
| Localized billing & tax | VAT/GST display, VAT number capture, e-invoicing mandates | **new** `regional-compliance-expert`, `integrations-engineer` | 4 |
| Locale-aware formatting | Multi-currency display, timezone-correct scheduling, number/date formatting (not UI translation) | **new** `i18n-currency-timezone` | 4 (architecture) / later (full translation) |
| Accessibility | WCAG 2.1 AA across core flows | `frontend-engineer` (extended) | Ongoing from Phase 0, audited at Phase 3 |
| Testing & quality gates | Unit/integration/e2e, AI eval sets, multi-tenant isolation tests | `qa-test-engineer` | Every phase |

## 4. Non-functional requirements ("professional grade" bar)

- **Availability**: target 99.9% for the core app; degrade gracefully when AI
  providers are slow/down (CRUD must keep working even if Claude API is unreachable
  — see `ai-features-architect`'s failure-mode guidance, extended by
  `devops-observability-expert` for the operational side).
- **Latency**: p95 < 500ms for core CRUD/list views; AI drafting/summarization
  streams a first token within ~1.5s rather than blocking on full generation.
- **Data durability**: automated Postgres backups, point-in-time recovery,
  documented RPO ≤ 1h / RTO ≤ 4h for v1 (tighten once there's a paying enterprise
  customer requiring more).
- **Security**: encryption in transit and at rest, secrets never in source,
  dependency/vuln scanning in CI, a path to SOC 2 Type II (access reviews, audit
  logging, vendor review) — don't need certification for v1, but don't build in a
  way that makes it unreachable later (this is why audit logging is in the schema
  from Phase 0, not bolted on in Phase 4).
- **Multi-tenant isolation**: zero cross-workspace data leaks — the single
  hardest requirement to relax later, so it's enforced at both app and DB layers
  from Phase 0 (`auth-security-expert`, `database-schema-expert`) and has a
  standing test requirement (`qa-test-engineer`).
- **Accessibility**: WCAG 2.1 AA on all core flows (not just "AI features" —
  the Kanban board's keyboard fallback, already specified in
  `pipeline-kanban-board`, is a concrete instance of this).
- **Observability**: every request traceable end-to-end (structured logs +
  tracing), every AI call logged with cost/latency/outcome for both debugging and
  unit-economics visibility, alerting on error-rate and latency SLO burn, not just
  uptime pings.

## 5. Explicitly out of scope (for now)

- Native mobile apps.
- On-prem/self-hosted deployment, single-tenant-per-customer infrastructure.
- Full multi-language UI translation (architecture must not block it; translated
  strings themselves are not a v1 deliverable).
- Marketing-automation-grade bulk email campaigns (this is a *sales* CRM with
  transactional/1:1 AI-drafted email, not a Mailchimp competitor).
- A user-facing "build your own object from scratch" builder beyond the
  custom-field/module system already scoped — full end-user schema design
  (Salesforce-style) is explicitly rejected as too complex for this team/stage
  (see `workspace-customization` skill, "what modular does not mean here").

## 6. Phased roadmap

- **Phase 0 — Foundations**: schema, auth, RBAC (owner/admin/member), core entity
  CRUD, Kanban pipeline, workspace-customization scaffolding (tables exist, UI can
  come later), CI/CD, baseline observability (error tracking + structured logs).
- **Phase 1 — AI-first core**: tool-calling framework, drafting, summarization,
  scoring, next-best-action, background job queue, AI eval harness.
- **Phase 2 — Connected & informed**: email/calendar sync *with consent/
  suppression gating built in from the start, not bolted on later*, CSV
  import/dedupe, notifications, search/saved views, billing.
- **Phase 3 — Insight & modularity**: reporting/dashboards, read models, vertical
  starter templates polished into a real onboarding flow, first optional module
  shipped end-to-end (proves the module pattern with a real vertical, e.g.
  real-estate listings), accessibility audit.
- **Phase 4 — Enterprise & platform**: SSO/SCIM, custom roles, public API +
  outbound webhooks, locale-aware formatting (multi-currency/timezone), regional
  privacy-law/data-residency review, localized billing/tax, SOC 2 readiness push.

## 7. Expert/skill coverage

Gap analysis against this scope, done alongside this document — three functional
areas had no owner in the existing agent roster:

- **Observability/reliability/deployment** — nothing previously covered CI/CD,
  infra, monitoring, alerting, or backup/DR. Added `devops-observability-expert`
  + `observability-and-slo` skill.
- **Reporting/analytics** — nothing previously covered dashboards, forecast
  reports, or the read-model/materialized-view work needed to make them fast at
  scale. Added `analytics-reporting-expert` + `reporting-read-models` skill.
- **Public API/developer platform** — `integrations-engineer` covers *consuming*
  third-party APIs; nothing covered exposing *this* product's data to customers'
  own systems (API keys, scopes, outbound webhooks, versioning). Added
  `api-platform-expert` + `public-api-and-webhooks` skill.

Additionally extended existing agents/skills rather than creating new ones for
areas that fit naturally within an existing owner's remit: enterprise auth
(SSO/SCIM/custom roles) into `auth-security-expert`; usage-based billing into
`integrations-engineer`; notifications into `backend-api-engineer` +
`frontend-engineer` with a new `notifications-and-digests` skill (cross-cutting,
not deep enough for its own agent); locale-aware formatting into a skill
(`i18n-currency-timezone`) consumed by `database-schema-expert` and
`frontend-engineer` rather than a new agent, since it's a set of conventions
more than an ongoing area of judgment calls.

**Second pass — legal/cultural market differences.** The first scoping pass
treated "different markets" mainly as geography-flavored i18n (currency,
timezone, formatting). A follow-up review surfaced that the more consequential
gap was regulatory, not linguistic: this product's AI can autonomously *send*
communications on a user's behalf, and communication-consent law (CAN-SPAM,
CASL, GDPR/ePrivacy, TCPA) varies by market in ways that create real legal risk
if defaulted wrong — plus data-residency/privacy-law variation beyond the
GDPR/CCPA baseline and localized billing/tax rules. Added
`regional-compliance-expert` (explicitly framed as engineering guardrails, not
legal advice) and `communication-consent-and-suppression` (the concrete
consent/suppression data model gating every send path). Unlike the other Phase
4 enterprise items, the consent-gating piece ships with email sending in
Phase 2 — it's a launch requirement for that feature, not a later hardening
pass.
