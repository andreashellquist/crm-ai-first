---
name: reporting-read-models
description: Pattern for building fast pipeline/forecast/activity reports and dashboards via background-refreshed summary tables instead of live aggregate queries against the transactional schema. Load this when implementing a report, dashboard widget, or export that aggregates across many Deal/Activity/Contact rows.
---

# Reporting read models

Owned by `analytics-reporting-expert`. The core schema (`crm-data-model`) is
tuned for per-record transactional access (the Kanban board, a contact detail
page); reporting needs wide aggregate scans, which is a different access
pattern that must not compete with live traffic on the same tables.

Implemented in `backend/CrmApi/Services/ReportingService.cs` and
`Controllers/ReportsController.cs`, with the dashboard UI in
`src/app/(app)/reports/page.tsx` — treat those as the reference
implementation of everything below.

## Shape

```csharp
// Models/PipelineSnapshot.cs — pipeline report: OPEN deals by stage
public class PipelineSnapshot
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string WorkspaceId { get; set; }
    public required string PipelineId { get; set; }
    public required string StageId { get; set; }
    public int DealCount { get; set; }
    public int DealValueCents { get; set; }
    public int WeightedValueCents { get; set; } // dealValueCents weighted by Stage.Probability
    public DateTime RefreshedAt { get; set; } = DateTime.UtcNow;
}

// Models/ForecastSnapshot.cs — forecast report: every deal, by forecast category
public class ForecastSnapshot
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string WorkspaceId { get; set; }
    public required string PipelineId { get; set; }
    public required string ForecastCategory { get; set; } // pipeline | best_case | commit | closed
    public int DealCount { get; set; }
    public int DealValueCents { get; set; }
    public int WeightedValueCents { get; set; }
    public DateTime RefreshedAt { get; set; } = DateTime.UtcNow;
}

// Models/ActivityMetric.cs — activity report: rolling 30-day window, by day + type
public class ActivityMetric
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string WorkspaceId { get; set; }
    public required DateOnly Date { get; set; }
    public required string Type { get; set; } // call | email | meeting | note
    public int Count { get; set; }
    public DateTime RefreshedAt { get; set; } = DateTime.UtcNow;
}
```

**Deviations from the general pattern, both deliberate:**

- **No `ownerId` column yet.** Reports can't be sliced by rep because no
  entity has an assignee/owner column at all — the same schema gap that
  blocks `deal_assigned`/`task_overdue` notifications (see
  `notifications-and-digests`). Add deal/task assignment first, then add
  `ownerId` here the same way.
- **No `periodStart`/time-series rows.** `PipelineSnapshot`/`ForecastSnapshot`
  hold *current* state only (one row per stage/category per workspace,
  fully replaced on every refresh) rather than a daily history — there's no
  "pipeline trend over time" report yet that would need historical rows.
  `ActivityMetric` is the exception: it's keyed by `Date` because the
  activity report itself is a daily breakdown, not because of a general
  time-series requirement.

Same idea applies to any future report family (e.g. a conversion/funnel
report) — one summary table per report family, not one giant catch-all
metrics table trying to serve every report shape. **The conversion/funnel
report from `analytics-reporting-expert`'s v1 set isn't built yet**: it
needs a stage-transition-history table (`Deal` only stores its *current*
`StageId`, never when it changed stages) that doesn't exist — build that
prerequisite first, don't approximate conversion rates from current-state
snapshots.

## Refresh strategy

- `ReportingService.RefreshWorkspaceReports(workspaceId)` recomputes all
  three tables for one workspace in a single transaction — delete existing
  rows for that workspace, insert freshly computed ones. This is a deliberate
  full-recompute rather than incremental updates, which makes the refresh
  trivially idempotent (running it twice, or out of order, produces the same
  result) at the cost of always scanning the whole workspace's open
  deals/recent activity rather than just what changed. Revisit only if a
  workspace's data volume makes a full recompute measurably slow.
- The `refresh_reports` job is enqueued by the writes that change what a
  report shows — `PipelineController.MoveDeal`, `UpdateDeal`, and
  `LogActivity` — not on a timer, since this app's `JobWorker` has no
  periodic/cron scheduling primitive, only explicitly-enqueued jobs. This is
  the "triggered by relevant writes" branch of the general pattern, not the
  "short interval" branch.
- `POST /api/reports/refresh` exists as a manual escape hatch for data that
  predates this feature or was seeded directly (`Data/Seed.cs` writes
  straight to the DB, bypassing the controllers that enqueue refreshes) —
  the "Refresh" button on the dashboard calls it.
- Every read-model table still carries `WorkspaceId` and the same
  indexing/isolation discipline as core tables (`database-schema-expert`) —
  this is aggregate data, not exempt data. See
  `MultiTenantIsolationTests.Reports_NeverReflectAnotherWorkspacesDeals`.

## Querying

`ReportsController` queries the read-model tables directly — never a live
`GROUP BY` over `Deal`/`Activity` for the dashboard. `GetBoard`'s pipeline
query (a live join for the Kanban board itself) is a different, legitimate
access pattern — per-record display, not aggregation — and isn't what this
skill is about.

## Export

`GET /api/reports/export?type=pipeline|forecast|activity` builds a CSV from
the exact same read-model query the on-screen dashboard uses
(`ReportsController.LoadReportData`), via a small hand-written `CsvWriter`
(counterpart to `CsvParser` — see the `csv-import-dedupe` skill) — one source
of truth for "what does this report say," reachable both on screen and as a
download. **The browser can't call this endpoint directly** — this app's
architecture has no CORS surface and the browser never holds the session
JWT (see `CLAUDE.md`'s "Chosen stack") — so the on-screen export link points
at a Next.js route handler (`src/app/api/reports/export/route.ts`) that
forwards the request server-to-server and streams the response back. This
bit us once during implementation: an `<a href="/api/reports/export?...">`
pointed straight at the pattern used for the .NET API's own path, which
works fine when curled with a manually-supplied token but 404s for a real
browser session with no route handler behind it — always verify a browser
download via an actual click in a real browser context, not just a curl
against the backend.

## Labels

Report/dashboard copy resolves entity names through the workspace's
`terminology` settings (per `workspace-customization`) — a forecast report
is user-facing text like any other page, not exempt from relabeling.
`ReportsController.Get` resolves `dealTerm`/`dealTermPlural` via
`TerminologyResolver` and includes them in `ReportsResponse` (e.g. "Listings
by stage" instead of "Deals by stage" for a relabeled workspace) rather than
hardcoding "Deal" in the API response or the frontend.
