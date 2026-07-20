# SOC 2 readiness — gap analysis

Phase 4 scope item, per `docs/PRODUCT_SCOPE.md` §4's non-functional bar ("a
path to SOC 2 Type II — access reviews, audit logging, vendor review — don't
need certification for v1, but don't build in a way that makes it
unreachable later"). This document is a gap analysis against SOC 2's Trust
Service Criteria (TSC), grounded in what's actually in this codebase today —
not a compliance certification, not legal/audit advice, and not something an
auditor has reviewed. Getting an actual SOC 2 report (Type I or Type II)
requires engaging a licensed CPA firm and, for Type II, a 6-12 month
observation period with evidence collection — this document identifies what
that engagement would find ready vs. missing if it started today, so the gap
is known rather than discovered mid-audit.

**Scope note**: this repo has no real deployed production infrastructure
(no live Vercel project, no managed Postgres instance, no actual customer
traffic) — it's a working prototype. Several items below are therefore
"can't verify, only assess the code/config that would matter once deployed"
rather than "confirmed working in production," and that distinction is
called out explicitly per item rather than glossed over.

## Which criteria apply

SOC 2's **Security** criterion (the "Common Criteria," CC1-CC9) is mandatory
for any report. A service organization chooses which of the four optional
criteria to add: **Availability**, **Confidentiality**, **Processing
Integrity**, **Privacy**. For a B2B CRM handling contact/deal PII, the
realistic scope is Security + Availability + Confidentiality — Processing
Integrity (accurate/complete/timely processing guarantees, more relevant to
payment/transaction processors) is unlikely to be in scope, and Privacy
overlaps heavily with `docs/REGIONAL_COMPLIANCE.md`'s data-subject-rights
gap (cross-referenced, not re-litigated here).

## Security (Common Criteria) — the mandatory core

| Control area | Status | Detail |
|---|---|---|
| Logical access control | **Built** | `RequireRoleAttribute`/`RequirePermissionAttribute` (`backend/CrmApi/Authorization/`) gate every workspace-configuration write at the point of mutation, not just in UI. Custom roles (`Models/Role.cs`) are an explicit permission subset of a fixed catalog — an admin can never self-grant more than an owner allows (`Authorization/Permissions.cs`). |
| Multi-tenant data isolation | **Built, standing test priority** | Every tenant-scoped table cascades on `WorkspaceId` (`Data/AppDbContext.cs`); `qa-test-engineer` treats cross-workspace leak tests as the highest-priority test category (`backend/CrmApi.Tests/MultiTenantIsolationTests.cs`). This is the single hardest SOC 2-relevant property to retrofit, and it's been enforced from Phase 0. |
| Authentication | **Built, one real limitation** | Session JWT (`AuthController`) + OIDC SSO (`SsoController`, JIT-provisioned) + SCIM deprovisioning (`ScimUsersController`, sets `WorkspaceMember.IsActive`). **Real gap**: no server-side session store — a deactivated member's already-issued JWT stays valid until its 7-day expiry (`auth-security-expert`'s stated limitation). An auditor will ask "how do you revoke an active session," and the honest answer today is "you can't, only block new logins." Closing this needs short-lived tokens + refresh, or a revocation list. |
| Audit logging | **Built this pass** | `Models/AuditLog.cs` + `Services/AuditLogService.cs` — a fixed catalog of security-relevant writes (member role changes/removal, role create/update/delete, API key create/revoke, SSO connection changes, workspace settings changes), each entry recording actor/action/target/timestamp, viewable read-only at `GET /api/audit-log` (owner/admin only). **Real, deliberate v1 gap**: the catalog is fixed and small (mirrors `WebhookSubscription.EventTypes`'s "small fixed catalog, not everything" discipline) — it does not log every read, every deal/contact CRUD, or login attempts (success or failure). An auditor doing access reviews will want login-attempt logging at minimum; that's a straightforward addition (same service, one more call site in `AuthController.Login`) not yet made. |
| Encryption in transit | **Assumed via hosting, not app-verified** | The frontend/backend split (`CLAUDE.md`'s "Chosen stack") means the browser never talks to the .NET API directly, and Vercel + a managed Postgres provider both terminate TLS by default — but this repo has no deployed instance to confirm actual certificate/TLS configuration against. Flag as "correct by architecture, unverified in a real deployment." |
| Encryption at rest | **Mixed — one real gap** | `ApiKey.HashedKey` is a one-way SHA-256 hash (never needs decrypting). **`SsoConnection.ClientSecret` and `WebhookSubscription.Secret` are stored in plaintext** — both already flagged in their respective feature docs (`i18n-currency-timezone`'s adjacent note, `public-api-and-webhooks`) as a tracked, not-yet-implemented follow-up (`auth-security-expert`'s "encrypt third-party credentials at rest" guidance). Database-provider-level encryption at rest (e.g. the managed Postgres provider's disk encryption) is a reasonable baseline but does not substitute for application-level encryption of these two columns specifically — a DB dump or a compromised read replica would expose them today. |
| Vulnerability/dependency scanning in CI | **Not built — real gap** | Checked `.github/workflows/ci.yml` in full and searched for a Dependabot config: neither exists. No `dotnet list package --vulnerable`, no `pnpm audit`, no Dependabot/Snyk/Renovate config anywhere in the repo. `docs/PRODUCT_SCOPE.md` §4 lists this as part of the professional-grade security bar; it is not currently met. This is one of the cheapest gaps to close (a GitHub Dependabot config file, or a CI step) relative to its audit value — worth prioritizing over harder items like session revocation. |
| Vendor/subprocessor management | **Not built — real gap** | No subprocessor list, no DPA (data processing agreement) tracking, anywhere in this repo. Real third-party processors already in use: Anthropic (every AI feature sends contact/deal/activity PII — see `docs/REGIONAL_COMPLIANCE.md` §4), the hosting/managed-Postgres provider (not yet chosen concretely), Sentry (wired but dormant, see below). An auditor's vendor-management control expects a maintained list of subprocessors with their own SOC 2 reports or equivalent — this is an organizational/process artifact, not code, and hasn't been started. |
| Periodic access reviews | **Mechanism exists, process doesn't** | `MembersController` (list/change-role/remove) and `AuditLogController` give an owner/admin the *tools* to review who has access to what — but there is no scheduled review workflow (a quarterly "confirm every member's role is still correct" process), and no code enforces one. This is a policy gap, not an engineering one: the primitives are built, "do this on a schedule" is not automated or even documented anywhere. |
| Incident response | **Not built — real gap** | No incident-response runbook, no per-severity escalation path, no breach-notification-timeline mapping exists in this repo. Same finding `docs/REGIONAL_COMPLIANCE.md` §4 surfaces from the privacy-law angle — this is the same gap viewed from SOC 2's angle: an auditor will ask for a written IR plan and evidence it's been exercised (a tabletop exercise, at minimum). Out of scope for this document to draft (it's a process document, not an engineering guardrail), but named here since it blocks Security-criterion readiness independent of any code change. |
| Secrets management (app config) | **Adequate for current stage** | JWT signing secret, Anthropic API key, DB connection string are read from configuration (`appsettings*.json`/environment variables), never hardcoded in source — confirmed via grep across `backend/CrmApi`. No secrets-manager integration (e.g. Azure Key Vault, AWS Secrets Manager) yet, which is a reasonable gap at this stage (single deployment, small team) but would come up in a real audit once there's a production secret rotation story to describe. |

## Availability

| Control area | Status | Detail |
|---|---|---|
| Uptime target | **Stated, not monitored** | `docs/PRODUCT_SCOPE.md` §4 states a 99.9% target for the core app. `GET /health` exists (`Program.cs`, anonymous, used as the e2e suite's readiness probe) and is described as "generally useful as an uptime check" — but there is no actual uptime monitoring/alerting service configured against it (no Pingdom/UptimeRobot/Vercel-native monitoring wired up in this repo). The target is aspirational until something is actually watching it. |
| Structured logging + tracing | **Built** | OpenTelemetry (ASP.NET Core + HttpClient + Npgsql instrumentation), console exporter in dev, OTLP if `Observability:OtlpEndpoint` is configured (`Program.cs`) — this is real, working instrumentation, not just a plan. |
| Error tracking | **Wired, dormant** | Sentry SDK is integrated (`Program.cs`'s `UseSentry`) but `Sentry:Dsn` ships blank — same "scaffold, not configured" pattern as Google OAuth/SSO, deliberately documented as a no-op until a real DSN is injected. This is a five-minute activation once there's a real Sentry project, not a code gap. |
| SLO-based alerting | **Not built** | `docs/PRODUCT_SCOPE.md` §4 calls for "alerting on error-rate and latency SLO burn, not just uptime pings" — no alerting rules exist anywhere in this repo (no PagerDuty/Opsgenie config, no Sentry alert rules, no OTel-collector-based alerting). This is real infrastructure work gated on having a real deployment and a real on-call story, appropriately deferred but worth naming as unmet. |
| Backup / disaster recovery | **Policy stated, unverified — no real DB to restore** | `docs/PRODUCT_SCOPE.md` §4 states target RPO ≤ 1h / RTO ≤ 4h with automated Postgres backups and point-in-time recovery. There is no actual production database to have backed up or restored — this is entirely a target/plan today, and `devops-observability-expert`'s own guidance ("a backup nobody has restored from is not a backup") means even once a managed Postgres provider is chosen, this control isn't "met" until a restore has actually been tested, not just configured. |

## Confidentiality

| Control area | Status | Detail |
|---|---|---|
| Workspace-scoped data access | **Built, strong** | Same multi-tenant isolation covered under Security above — every read/write path re-scopes by `WorkspaceId`, including the external-facing public API and SCIM surfaces (`ApiKeyAuthenticationHandler` resolves to the same `workspaceId` claim session auth does). |
| PII kept out of logs | **Built, verified** | Spot-checked across `Services/*.cs`: log statements interpolate opaque IDs (`dealId`, `workspaceId`), never raw `Contact.Email`/activity body text — matches `CLAUDE.md`'s working convention ("never log full PII... redact it in any prompt sent to a third-party eval/analytics service"). |
| Data classification | **Not formally done** | No document anywhere labels which fields are "Confidential" vs. "Restricted" vs. "Public" in the SOC 2 sense — the working convention above treats PII carefully in practice, but there's no written data-classification policy an auditor could point to. Cheap to write down (a table of entity/field → sensitivity), not yet done. |

## Summary: what's actually blocking a real SOC 2 engagement today

Roughly in order of effort-to-close:

1. **Cheap, code-only, do soon**: Dependabot/vuln scanning in CI; login-attempt entries added to the audit log catalog; a written data-classification table.
2. **Cheap-ish, code + a little design**: application-level encryption for `SsoConnection.ClientSecret`/`WebhookSubscription.Secret` (same fix serves both, already tracked).
3. **Real engineering work, deferred appropriately**: session/token revocation (short-lived tokens + refresh, or a revocation list); SLO-based alerting once there's a real deployment to alert on.
4. **Organizational/process work, not code at all**: incident-response runbook, vendor/subprocessor management, a periodic access-review cadence, backup restore testing once real infrastructure exists. These are the items a SOC 2 Type II audit weighs most heavily (evidence of *operating* a control over time, not just having built it) and they cannot be "implemented" by writing code — they need policies, a named owner, and time.

**Bottom line**: the engineering foundation this document was asked to assess
against is in reasonable shape for a company at this stage — multi-tenant
isolation and RBAC (the hardest-to-retrofit pieces) have been correct from
Phase 0, and audit logging closes a real, previously-true gap between
`docs/PRODUCT_SCOPE.md`'s claim ("audit logging is in the schema from Phase
0") and what actually existed in code before this pass. But "SOC 2 readiness"
is mostly not a code problem from here — it's process, policy, and organizational
maturity that has to be built and *operated*, not shipped in a pull request.
