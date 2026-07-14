---
name: observability-and-slo
description: Conventions for structured logging, tracing, error tracking, AI-call telemetry, and SLO-based alerting in this CRM. Load this when adding a new Server Action, background job, webhook handler, or LLM call, so it's observable the same way as everything else instead of being a blind spot the first time it breaks in production.
---

# Observability & SLOs

Owned by `devops-observability-expert`; see `docs/PRODUCT_SCOPE.md` §4 for the
actual SLO targets (99.9% availability, p95 < 500ms core CRUD).

## Every request/job needs

- A **trace/request ID** propagated through Server Action → DB calls →
  background job → any outbound API call, so one failure can be followed
  end-to-end instead of correlated by guesswork across separate log lines.
- A **structured log entry** at entry/exit (or on error) including
  `workspaceId` (when applicable), the trace ID, and outcome — never raw PII
  (email bodies, phone numbers) in the log payload; log record IDs and redact/
  truncate content, per `auth-security-expert`.
- Clear separation of **expected failures** (validation error, not-found,
  permission denied — log at `info`/`warn`, no alert) from **unexpected
  failures** (DB error, third-party 5xx, unhandled exception — log at `error`,
  reported to the error tracker), per `backend-api-engineer`'s error taxonomy.

## AI calls specifically

Every Claude API call logs: which feature triggered it (drafting/scoring/
summarization/agentic-tool-loop), latency, input/output token counts, which
tools were called (names only, not full arguments if they contain PII), and
outcome (success / tool-call / error / timeout). This is the data
`ai-features-architect`'s cost-discipline guidance and
`devops-observability-expert`'s cost-watching both depend on — treat it as a
required field of shipping an AI feature, not optional instrumentation.

## Background jobs

Jobs log start/end/outcome and duration; a job queue's depth and oldest-pending-
job age are metrics worth alerting on directly (a growing backlog is a leading
indicator of trouble before anything actually times out or errors).

## Alerting shape

Alert on **symptom + trend**, not individual events:

- Error-rate or latency crossing an SLO-burn threshold over a rolling window —
  not "an error happened."
- Job queue depth/age exceeding a threshold.
- Webhook/integration delivery failure rate exceeding a threshold (ties to
  `api-platform-expert`'s outbound webhook retry/visibility requirements).
- Backup failure — always page-worthy, no threshold needed (per
  `devops-observability-expert`'s backup/DR requirements).

A single user's validation error, a single retried-and-succeeded job, or a
single slow-but-under-timeout request should never independently page anyone —
if it does, the threshold is wrong, fix the threshold rather than training
people to ignore alerts.
