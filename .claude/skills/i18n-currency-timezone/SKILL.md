---
name: i18n-currency-timezone
description: Conventions for multi-currency, timezone-correct scheduling, and locale-aware number/date formatting in this CRM, so the architecture doesn't block serving customers across geographic markets even though full UI translation isn't a v1 deliverable. Load this when touching Deal amounts, dates/times shown to users, or any place a number/date is formatted for display.
---

# Locale-aware formatting: currency, timezone, numbers/dates

**Scope note**: despite the filename, this is *not* language localization
(translated UI strings) — that's explicitly deferred per `docs/PRODUCT_SCOPE.md`
and out of scope until there's real demand. This skill is the narrower, cheap-
to-get-right-early part: don't bake in USD/UTC/en-US assumptions at the data
layer. It's also not where legal/cultural differences between markets belong —
communication-consent law, data residency, and localized tax/billing rules are
`regional-compliance-expert`'s territory (see `communication-consent-and-
suppression` for the consent-gating pattern specifically); this skill is display
formatting only, not compliance.

**Built**: `Workspace.DefaultCurrency` (workspace-level fallback, per-`Deal`
`Currency` override), `WorkspaceMember.Timezone` (per-user IANA identifier),
and `MeController` (`GET`/`PUT /api/me`) for self-service profile — treat
those, plus `ReportsController.Get()`'s `FormatInCallerTimezoneAsync` helper,
as the reference implementation of everything below. Frontend at
`src/app/(app)/settings` ("My profile" and "Default currency" sections) and
`src/lib/money.ts` (`formatAmount`).

## Currency

```csharp
public class Workspace
{
    // ...
    public string DefaultCurrency { get; set; } = "USD"; // ISO 4217, e.g. "EUR"
}

public class Deal
{
    // ...
    public int? AmountCents { get; set; }
    public string? Currency { get; set; } // per-deal override; falls back to Workspace.DefaultCurrency when null
}
```

- `Deal.AmountCents` is paired with an optional per-`Deal` `Currency`
  override; when it's `null`, the workspace's `DefaultCurrency` applies. Never
  assume `"USD"` in code — always read the applicable currency and format
  accordingly. `WorkspaceSettingsController`'s `PUT /api/workspace/settings`
  validates `DefaultCurrency` against a 3-letter ISO 4217 pattern
  (`^[A-Z]{3}$`) before persisting; owner/admin only, same
  `[RequireRole("owner", "admin")]` gate as terminology/module changes.
- Every endpoint that returns deal amounts also returns the workspace's
  `DefaultCurrency` alongside them (`PipelineBoardDto.DefaultCurrency`,
  `ReportsResponse.DefaultCurrency`) — a DTO carrying a bare `AmountCents`
  with no currency context is a formatting bug waiting to happen the moment a
  workspace isn't USD.
- Store amounts as integer minor units (cents) in the deal's own currency; do
  **not** store a pre-converted "normalized USD" value on the record itself. If
  cross-currency aggregation is needed for a report (`analytics-reporting-expert`),
  convert at query/read-model-refresh time using a rate lookup, and be explicit
  in the UI that aggregated totals are converted estimates, not the literal deal
  values. (Not yet needed: this CRM's reports currently sum within a single
  workspace's own pipeline, which the vast majority of the time uses one
  currency throughout.)
- Format currency for display with `Intl.NumberFormat` given the relevant
  currency code — never hand-roll `$` + comma formatting, which breaks for
  every non-USD currency. `src/lib/money.ts`'s `formatAmount(cents, currency,
  fallbackCurrency)` takes the fallback as a **required** parameter rather
  than defaulting to `"USD"` internally — every call site must pass the
  workspace's real `DefaultCurrency`, so a non-USD workspace's amounts never
  silently render with the wrong symbol. There is no bare
  `formatAmount(cents, currency)` two-argument form; if a new call site only
  has `deal.currency` and no workspace context in scope, that's a sign the
  surrounding page needs to fetch `/api/workspace/settings` (or thread
  `PipelineBoardDto.DefaultCurrency`/`ReportsResponse.DefaultCurrency` through),
  not a reason to hardcode a fallback.

## Timezone

```csharp
public class WorkspaceMember
{
    // ...
    public string Timezone { get; set; } = "UTC"; // IANA identifier, e.g. "America/New_York"
}
```

- Store all timestamps in UTC in the database (every `DateTime` column is
  `DateTime.UtcNow`-sourced) — never store a "local" wall-clock time.
- Every `WorkspaceMember` has an explicit IANA timezone identifier, not a UTC
  offset (offsets shift with DST; a fixed offset silently goes wrong twice a
  year). `MeController`'s `PUT /api/me` validates it via
  `TimeZoneInfo.FindSystemTimeZoneById` (throws `TimeZoneNotFoundException`
  on anything that isn't a real IANA id — caught and turned into a 400)
  before persisting, and a member manages their own via the `/settings`
  page's "My profile" section (`MyProfileForm`) — there's no admin-editable
  "set another member's timezone" surface, matching `MeController`'s scoping
  to the caller's own `WorkspaceMember` row.
- Convert to the viewing user's timezone at render/prompt time using that
  field, never a browser-guessed value alone for anything that's stored or
  scheduled server-side (a scheduled `TaskItem`'s due-date reminder must fire
  correctly regardless of which browser created it). Reference conversion
  (`ReportsController.FormatInCallerTimezoneAsync`):
  ```csharp
  var timezoneId = await db.WorkspaceMembers
      .Where(m => m.WorkspaceId == current.WorkspaceId && m.UserId == current.UserId)
      .Select(m => m.Timezone)
      .FirstOrDefaultAsync() ?? "UTC";
  var timezone = TimeZoneInfo.FindSystemTimeZoneById(timezoneId); // wrap in try/catch → UTC fallback; a corrupt/renamed IANA id must never 500 the request
  var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcValue, DateTimeKind.Utc), timezone);
  ```
  `ReportsResponse.RefreshedAtDisplay` (a pre-formatted string, not a raw
  `DateTime`) is the shape this produces: the backend does the conversion,
  the frontend just renders the string — keeping "what timezone is this
  server?" out of client-side `Date` handling entirely, per the next bullet.
- **Prefer formatting on the backend, in the caller's timezone, over shipping
  a raw UTC timestamp for the frontend to call `.toLocaleString()` on** — the
  latter silently uses the *browser's* timezone, which is only correct by
  coincidence (matches the logged-in device, not necessarily the
  `WorkspaceMember.Timezone` the user actually configured, e.g. viewing from
  a shared/kiosk machine or a different device than usual). This is why
  `reports/page.tsx` renders `report.refreshedAtDisplay` rather than
  `new Date(report.refreshedAt).toLocaleString()` — the small number of other
  remaining `new Date(...).toLocaleString()` call sites in the frontend
  (activity timestamps, webhook delivery times) are pre-existing and not yet
  migrated to this pattern; migrate them opportunistically when next touched,
  not as a backlog item to batch-fix.
- AI-drafted content referencing dates/times ("let's meet Tuesday at 2pm")
  must resolve relative to the *recipient's* timezone context where known, not
  the server's or the drafting user's — get this from the Contact/Activity
  context passed into the prompt (`ai-features-architect`).

## Locale-aware formatting

- Dates, numbers, and names format via `Intl` APIs keyed off the viewing user's
  locale (default to a sensible fallback like `en-US` when unset), not
  hardcoded `MM/DD/YYYY`-style formatting — this is what keeps the door open for
  non-US customers without a rewrite. There is no per-user locale field yet
  (only `Timezone`) — `formatAmount` and the backend's
  `FormatInCallerTimezoneAsync` both currently hardcode `"en-US"`/invariant
  formatting for number grouping/date layout while still using the *real*
  currency code and timezone; adding a `WorkspaceMember.Locale` column
  alongside `Timezone` is the natural follow-up once a customer actually
  needs non-`en-US` number/date layout, not a hidden gap today.
- Don't assume name order (first/last) is universal in UI copy or AI-generated
  prose beyond what's already modeled (`FirstName`/`LastName` on `Contact`) —
  keep this on the radar for later but don't over-build for it now; the
  concrete near-term requirement is currency and timezone correctness, not a
  full locale-aware name-formatting system.

## What this does not require yet

No translated UI strings, no locale-switcher in v1 — this skill is about not
baking in USD/UTC/en-US assumptions at the data and formatting layer, which is
cheap to avoid now and expensive to retrofit, not about shipping translations
nobody has asked for yet (per `docs/PRODUCT_SCOPE.md`'s scoping discipline).
