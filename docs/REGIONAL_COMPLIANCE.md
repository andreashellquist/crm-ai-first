# Regional privacy law & data residency

Phase 4 scope item, per `docs/PRODUCT_SCOPE.md` row "Regional privacy law &
data residency" and the `regional-compliance-expert` agent charter
(`.claude/agents/regional-compliance-expert.md`). This document identifies
requirements and where engineering guardrails must sit. **It does not
implement anything, and it is not legal advice.** Any workspace operating in
a regulated industry (health, finance, insurance — three of this product's
named verticals per `docs/PRODUCT_SCOPE.md` §1) or selling into a market
outside the ones characterized below should get real legal counsel before
relying on this document alone. Where this doc assesses a specific regime,
it says what's structurally different from the GDPR/CCPA baseline this app
already assumes and flags it for review — it does not assert "compliant" or
"not compliant" for any market.

Companion doc: `.claude/skills/communication-consent-and-suppression/SKILL.md`
covers consent/suppression law (CAN-SPAM, CASL, GDPR/ePrivacy, TCPA) — that's
a separate concern (regulating *sending*, not *storing*) and ships with
email/calendar sync in Phase 2, not here. This document is about data
residency and privacy-law variation in how PII is *stored and processed*
beyond the GDPR/CCPA baseline `auth-security-expert` already owns the
mechanics for.

## 1. What "data residency" actually means as a requirement, and who hits it first

"Data residency" collapses three distinct asks that get conflated in sales
conversations — worth separating because they have different engineering
answers:

1. **Legal data-localization** — a law requires certain data to physically
   stay within a country's borders (e.g. PIPL in China). This is a hard
   infrastructure requirement, not negotiable by contract language.
2. **Contractual/procurement residency** — no law requires it, but a
   customer's own policy or a government-procurement requirement (e.g. an EU
   public-sector buyer, a company with an internal "EU data stays in EU"
   policy) makes it a deal-blocking checkbox regardless of what's legally
   required.
3. **Sovereignty-adjacent concerns** — a customer worried less about *where*
   data sits and more about *which jurisdiction's courts/government* can
   compel access to it (e.g. US CLOUD Act reach into a US-headquartered
   vendor's EU-hosted data). This one isn't solved by regional hosting alone.

All three show up as "can you guarantee our data stays in \[region\]?" in a
sales call, but only #1 is a legal must; #2 and #3 are negotiable/contractual
until a specific customer makes them a blocker.

**Which of this product's verticals hit this first**, per `PRODUCT_SCOPE.md`
§1's target verticals (B2B SaaS sales, recruiting/staffing, real estate
brokerage, insurance agencies):

- **Insurance** is the most exposed — it's a regulated industry in every
  market it operates in, often with sector-specific data-handling rules on
  top of general privacy law (and is explicitly called out as needing real
  legal counsel per the agent charter's hard boundary, independent of
  region).
- **Recruiting/staffing** handles candidate PII, which in the EU is
  frequently treated as more sensitive than ordinary B2B contact data
  (candidate data touches employment-law-adjacent processing purposes), and
  recruiting agencies are more likely than a SaaS sales team to have EU
  public-sector or enterprise clients with explicit residency riders in
  their vendor contracts.
- **Real estate** is the least likely to hit this first — brokerage data is
  typically lower-sensitivity and less cross-border by nature (a listing is
  tied to a physical property in one jurisdiction already).
- **B2B SaaS sales** sits in the middle — mid-market/SMB targets (per
  `PRODUCT_SCOPE.md` §1) are unlikely to demand residency, but an
  enterprise or public-sector deal in any vertical can raise it regardless
  of which vertical the workspace is in — this is a deal-size signal more
  than a vertical signal.

**Practical read**: don't build residency infrastructure speculatively for a
vertical; treat the first concrete enterprise/government deal that requires
it as the trigger, per `PRODUCT_SCOPE.md` §1's stated deployment assumption
("single multi-tenant SaaS deployment... revisit only if an enterprise deal
specifically requires data residency this can't satisfy").

## 2. Regimes beyond the GDPR/CCPA baseline

`auth-security-expert` owns the *mechanics* of PII handling and data-subject
requests (export/delete) against a GDPR/CCPA baseline. The regimes below are
characterized by how they diverge from that baseline — substance over an
exhaustive list.

### UK GDPR (post-Brexit divergence) — materially the same baseline, one real gap

The UK "onshored" GDPR after Brexit (UK GDPR + Data Protection Act 2018), and
it tracks EU GDPR closely — for engineering purposes, treat UK data subjects
under the same consent/access/erasure model already assumed for EU. The one
genuine divergence: **cross-border transfer mechanics**. EU→UK and UK→EU
transfers currently rely on separate (if mutually recognized) adequacy
decisions, not automatically covered by whatever transfer mechanism is used
for EU→US (e.g. Anthropic's data processing terms — see §4). This matters
the moment the product has a specific UK enterprise customer asking about
transfer safeguards; it is not a reason to change anything about current
single-region deployment today.

### PIPEDA (Canada) — materially similar, real divergence in breach notification and consent model

PIPEDA's baseline (consent-based collection, purpose limitation, access
rights) is close enough to GDPR/CCPA that the existing data model doesn't
need new fields to be *structurally* compatible. Two real differences worth
flagging for review when a Canadian customer or workspace comes up:

- **Meaningful consent** — PIPEDA's guidance on what counts as valid consent
  is more prescriptive about plain-language disclosure at the point of
  collection than CCPA's opt-out model; this affects consent-capture UI
  copy, not schema.
- **Mandatory breach reporting** — PIPEDA requires notifying the Privacy
  Commissioner of Canada and affected individuals for breaches posing "real
  risk of significant harm," on its own timeline separate from GDPR's
  72-hour rule. This is an incident-response-process gap (who gets notified,
  on what timeline, per jurisdiction of affected data subjects), not a data
  model gap — worth a line in whatever breach-response runbook exists
  eventually, out of scope for this document to draft.
- Note: CASL (Canada's anti-spam law) is a *separate* regime already covered
  in `communication-consent-and-suppression` — don't conflate PIPEDA (data
  handling) with CASL (sending consent) when a Canadian deal raises "Canadian
  compliance" generically; ask which one they mean.

### LGPD (Brazil) — materially similar structure, one operational divergence

Brazil's LGPD is explicitly modeled on GDPR (legal bases, data-subject
rights, a data protection officer requirement above a certain scale) — the
closest of the regimes covered here to something this app already assumes it
needs to satisfy for EU customers. Real divergence: LGPD's enforcement body
(ANPD) and its own breach-notification timeline are separate obligations
from GDPR's, so "we already do this for GDPR" is necessary but not
sufficient — it needs its own confirmation once a Brazilian customer is
real, not a redo of the underlying mechanics. (Also relevant to Brazil,
orthogonally: e-invoicing mandates — that's `integrations-engineer`/
localized-billing territory, not this document's.)

### APAC — flagged, not deep-dived (breadth over exhaustiveness)

- **Singapore PDPA** — consent-based, materially similar to GDPR/CCPA in
  structure; Singapore's PDPC also has its own breach-notification
  thresholds. No known divergence significant enough to require a schema
  change; treat as a "confirm when it comes up" item like PIPEDA/LGPD above.
- **Australia Privacy Act** — currently under active reform (increased
  penalties, a more explicit statutory tort for serious privacy invasions
  under recent amendments) — the direction of travel is toward GDPR-like
  strictness, worth re-checking at the time a real Australian deal appears
  rather than assuming today's characterization stays current.
- **PIPL (China)** — the one regime here that's *not* materially similar to
  the baseline. PIPL imposes real data-localization: certain categories of
  personal information collected in China must be stored in China, with
  government security-assessment requirements before any cross-border
  transfer. This is the one regime on this list where "characterize the
  divergence" is insufficient — it's an infrastructure decision (see §3) as
  soon as it's real, not a policy/process adjustment like the others above.

### What's deliberately not covered here

POPIA (South Africa) is named in the `regional-compliance-expert` agent
charter as a regime to characterize on demand but isn't deep-dived in this
pass — no current signal (named vertical, actual prospect) that it's a
near-term concern; add a subsection here the first time it's asked about
rather than speculatively researching it now, consistent with the agent
charter's "don't block shipping into a region waiting for exhaustive
research" instruction.

## 3. Engineering guardrails: cheap now vs. genuinely deferred

**Actually building multi-region infrastructure is explicitly out of scope
right now** (`PRODUCT_SCOPE.md` §1's deployment-model assumption, §5's
out-of-scope list). The question this section answers is narrower: is there
anything cheap to do *today* that keeps a future "pin this workspace's data
to a region" option open, without building the option itself.

### Cheap now (do before it's expensive to retrofit)

- **A `Workspace.DataRegion` (or similarly named) nullable/default field is
  worth adding the next time `Workspace` is touched for an unrelated reason**
  — not a migration to schedule on its own, but if a migration touching
  `Workspace` is already happening, adding a column that defaults to the
  single current region (e.g. `"us"`) costs nothing today and gives a real
  place to hang a future decision, the same "cheap now, expensive to
  retrofit" logic that motivated `Workspace.DefaultCurrency` and
  `WorkspaceMember.Timezone` existing in the schema since early phases
  before anything read or wrote them (see `i18n-currency-timezone`'s
  documented history of exactly that pattern). **Do not build the read/write
  logic, the region-picker UI, or the actual multi-region deployment now**
  — that's the deferred half below. This is explicitly a "next incidental
  touch," not a scheduled migration — don't create a migration whose only
  purpose is this column.
- **Keep every tenant-scoped table's `WorkspaceId` FK as the sole
  partitioning key it already is.** This is already true (`database-schema-
  expert`'s convention, applied consistently — see `AppDbContext.cs`'s
  `WorkspaceId`-scoped cascade deletes across every tenant table). A future
  region-pinning story (whether that's a separate database per region, a
  Postgres logical-replication split, or a fully separate deployment per
  region) is straightforward *because* every tenant table already keys off
  one column with no cross-tenant joins baked into the schema — nothing new
  needs to happen here, but any future schema change that broke this
  invariant would foreclose the option, so it's worth naming as a constraint
  to protect rather than a task to do.
- **Don't hardcode "single database" assumptions into application code
  beyond the connection string.** Grepped for this: `AppDbContext` is
  configured from a single `ConnectionStrings:Default`-shaped value in
  `Program.cs`/`appsettings*.json` with no code elsewhere assuming there's
  exactly one physically reachable Postgres instance. That's already the
  right shape — a future per-region connection string is a configuration
  change, not a refactor, as long as nothing new starts assuming a single
  global connection.

### Genuinely deferred (do not build until a real deal requires it)

- **Actual multi-region deployment** (a second Postgres instance in an EU
  region, routing logic to pick a workspace's database at connection time,
  region-aware backup/DR) — real infrastructure lead time, correctly out of
  scope per `PRODUCT_SCOPE.md` §1 and §5. When a real deal requires it, loop
  in `devops-observability-expert` (deployment topology, backup/DR per
  region) and `database-schema-expert` (whether it's a single sharded
  database, one database per region, or another shape) immediately, per the
  agent charter's explicit instruction — this is lead-time-sensitive
  infrastructure work, not something to discover after a deal is signed.
- **A region-picker at workspace creation** — no UI or provisioning logic
  for this should be built until there's a real backend to route to; a
  region picker with only one working region behind it is worse than no
  picker (it implies a guarantee the infrastructure doesn't back).
- **Per-region encryption-key management, per-region subprocessor
  agreements** — genuinely deferred; these follow from, not precede, an
  actual regional deployment decision.

## 4. What exists today vs. real gaps

Grepped the codebase (`backend/CrmApi`) rather than assuming baseline
coverage. Findings:

### Exists

- **Multi-tenant isolation** — every tenant-scoped table cascades on
  `WorkspaceId` (`backend/CrmApi/Data/AppDbContext.cs`), and this is the
  standing highest-priority test category per `qa-test-engineer`. Not a
  residency control by itself, but the prerequisite structural property any
  future residency work builds on (see §3).
- **PII kept out of logs** — spot-checked `EmailDraftingService.cs` and
  other `Services/*.cs` call sites: log statements interpolate `dealId`/
  `workspaceId` (opaque IDs), never `Contact.Email`/`Activity` body text.
  This matches the CLAUDE.md working convention ("never log full PII, and
  redact it in any prompt sent to a third-party eval/analytics service") and
  held up under a grep for `Email`/`PII`/`redact` across `Services/`.
- **Soft-delete columns exist on the three main PII-bearing entities** —
  `Contact.DeletedAt`, `Company.DeletedAt`, `Deal.DeletedAt`
  (`backend/CrmApi/Models/`) — `ContactsController.List` already filters on
  `DeletedAt == null`.
- **OIDC-based SSO** now exists (`Controllers/SsoController.cs`,
  `Models/SsoConnection.cs`) — not a residency control, but relevant if a
  future residency-driven deployment ever needs to reason about where
  identity-federation traffic (IdP discovery calls, token exchange) flows;
  worth a note to `auth-security-expert` if that becomes concrete. No SAML
  support exists (OIDC only).

### Real gaps (concrete, not speculative)

- **No data-subject export or deletion endpoint exists at all.**
  `ContactsController` has `List`, `Create`, and CSV-import endpoints — no
  `DELETE` action anywhere in the file, and no export-a-single-contact's-
  data endpoint anywhere in the codebase (`ReportsController.Export` exports
  aggregate report data, not an individual's PII). `Contact.DeletedAt`,
  `Company.DeletedAt`, and `Deal.DeletedAt` are consequently **dead columns
  today** — schema exists, nothing reads or writes them outside of
  `ContactsController.List`'s filter — the same "field exists in the schema,
  nothing populates it yet" pattern `CLAUDE.md` already documents for
  `Workspace.DefaultCurrency`/`WorkspaceMember.Timezone` before the Phase 4
  i18n work closed that gap. This is squarely `auth-security-expert`'s
  "mechanics of data-subject requests" remit (see that agent's "Data-subject
  requests" section: "design contact/company deletion as a real cascade...
  from day one... GDPR/CCPA-style 'delete my data' requests are a
  when-not-if") — flagging it here because a workspace in any of the regimes
  in §2 (all of which include an access/erasure right in some form) will
  eventually ask for this, and today there is no way to fulfill that request
  except a direct database operation.
- **No `Workspace`-level region/jurisdiction field at all** — confirmed by
  reading `Models/Workspace.cs` in full: `Id`, `Name`, `DefaultCurrency`,
  timestamps, and navigation properties, nothing else. Not a gap to close
  now (see §3 — this is the "cheap now" item, not yet done), but worth
  stating plainly: there is currently no data anywhere in the schema
  recording which jurisdiction a workspace or its data subjects are in,
  which means even *characterizing* which regime applies to a given
  workspace today is a manual/sales-conversation fact, not something the
  product tracks.
- **Every AI feature sends contact/deal/activity PII to a third-party
  processor (Anthropic) with no region control.** Confirmed in
  `Services/AnthropicMessagesClient.cs`: the client is constructed with only
  an API key, no region/endpoint configuration, and `Services/
  EmailDraftingService.cs`, `DealScoringService.cs`, `SummarizationService.cs`,
  and `NextBestActionService.cs` all serialize real contact names/emails,
  deal amounts, and activity free-text into the prompt sent to that API (per
  the `ai-features-architect` skill's grounding pattern — this is the
  feature working as designed, not a bug). For any regime with cross-border-
  transfer restrictions (UK GDPR, LGPD, and especially PIPL — see §2), this
  is the concrete mechanism by which workspace PII leaves whatever region it
  was collected in, independent of where the Postgres database itself lives.
  This is not something to fix speculatively (no region has been confirmed
  to require blocking it), but it is the first thing to check the moment a
  PIPL-relevant customer (or any customer requiring a signed subprocessor/
  transfer agreement covering AI processing) is a live conversation — it's a
  vendor-agreement and possibly a feature-gating question ("disable AI
  features for this workspace's region"), not a schema change.
- **No breach-notification runbook of any kind exists in this repo** — no
  incident-response doc, no per-jurisdiction notification-timeline mapping.
  Out of scope for this document to draft (it's a process document, not an
  engineering guardrail), but named here since §2 surfaces that at least
  three of the regimes covered (PIPEDA, LGPD, GDPR/UK GDPR) have their own
  breach-notification obligations on different timelines.

## 5. Summary table

| Regime | Divergence from GDPR/CCPA baseline | Action now | Trigger for real work |
|---|---|---|---|
| UK GDPR | Structurally same; transfer-mechanism nuance EU↔UK | None | Specific UK enterprise deal asking about transfer safeguards |
| PIPEDA (Canada) | Similar; stricter "meaningful consent" copy, separate breach-notification timeline | None | Canadian customer/incident |
| LGPD (Brazil) | Structurally very similar (GDPR-modeled); separate enforcement body/timeline | None | Brazilian customer |
| Singapore PDPA | Similar structure | None | Confirm when it comes up |
| Australia Privacy Act | Similar today, tightening via active reform | None | Re-check at time of a real AU deal |
| PIPL (China) | **Real divergence** — mandatory in-country data localization | None (infra) | Any concrete China-market deal — loop in `devops-observability-expert`/`database-schema-expert` immediately |
| POPIA (South Africa) | Not characterized in this pass | None | First real signal (named prospect) |

## What this document deliberately does not do

- It does not add a `Workspace.DataRegion` column, a data-export endpoint, or
  any other code — those are `database-schema-expert`/`auth-security-expert`/
  `backend-api-engineer` implementation work, triggered by the gaps and
  guardrails named in §3 and §4.
- It does not assert compliance status for any market — per the
  `regional-compliance-expert` agent's hard boundary, this is a
  structural-divergence characterization, not a legal conclusion.
- It does not re-derive communication-consent law (CAN-SPAM/CASL/GDPR-
  ePrivacy/TCPA) — see `.claude/skills/communication-consent-and-suppression/
  SKILL.md`, which ships with email sending in Phase 2.
- It does not cover VAT/GST/e-invoicing — that's the "Localized billing &
  tax" line item in `PRODUCT_SCOPE.md`'s same Phase 4 row group, owned by
  `regional-compliance-expert` jointly with `integrations-engineer`, and is
  a separate document/pass.
