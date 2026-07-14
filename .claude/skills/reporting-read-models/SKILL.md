---
name: reporting-read-models
description: Pattern for building fast pipeline/forecast/activity reports and dashboards via background-refreshed summary tables instead of live aggregate queries against the transactional schema. Load this when implementing a report, dashboard widget, or export that aggregates across many Deal/Activity/Contact rows.
---

# Reporting read models

Owned by `analytics-reporting-expert`. The core schema (`crm-data-model`) is
tuned for per-record transactional access (the Kanban board, a contact detail
page); reporting needs wide aggregate scans, which is a different access
pattern that must not compete with live traffic on the same tables.

## Shape

```prisma
model PipelineSnapshot {
  id           String   @id @default(cuid())
  workspaceId  String
  pipelineId   String
  stageId      String
  ownerId      String?  // rep, if reports are sliced by owner
  periodStart  DateTime // e.g. start of day the snapshot represents
  dealCount    Int
  dealValueCents Int
  weightedValueCents Int // dealValueCents-equivalent weighted by stage.probability

  @@index([workspaceId, pipelineId, periodStart])
}
```

Same idea applies to activity/conversion reporting (`ActivityMetricsDaily`,
`StageConversionDaily`, etc.) — one summary table per report family, not one
giant catch-all metrics table trying to serve every report shape.

## Refresh strategy

- A background job (per `backend-api-engineer`'s job conventions) recomputes
  snapshot rows — either triggered by relevant writes (a deal's stage changes →
  enqueue a recompute for that workspace+pipeline) or on a short interval (every
  few minutes), whichever fits the report's freshness need. Forecast/pipeline
  reports tolerate a few minutes of staleness; don't over-engineer real-time
  precision nobody asked for.
- Recompute jobs are idempotent and scoped to one workspace (and ideally one
  pipeline/period) per invocation — never a single job that walks every
  workspace serially, since one large workspace's compute time would then delay
  every other workspace's report freshness.
- Read models still carry `workspaceId` and follow the same indexing/isolation
  discipline as core tables (`database-schema-expert`) — this is aggregate data,
  not exempt data.

## Querying

Dashboards and report endpoints query the read-model tables directly — never
fall back to a live `GROUP BY` over `Deal`/`Activity` for the default view. A
live aggregate query is acceptable only for a genuinely ad hoc, low-traffic case
(e.g. an admin debugging tool), never for a page real users load routinely.

## Export

Every report backed by a read model should have a CSV export path that queries
the same read model (not a separate export-specific query) — one source of
truth for "what does this report say," reachable both on screen and as a
download.

## Labels

Report/dashboard copy resolves entity names through the workspace's
`terminology` settings (per `workspace-customization`) — a forecast report is
user-facing text like any other page, not exempt from relabeling.
