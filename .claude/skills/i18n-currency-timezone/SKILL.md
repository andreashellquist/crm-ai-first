---
name: i18n-currency-timezone
description: Conventions for multi-currency, timezone-correct scheduling, and locale-aware formatting in this CRM, so the architecture doesn't block serving customers across geographic markets even though full UI translation isn't a v1 deliverable. Load this when touching Deal amounts, dates/times shown to users, or any place a number/date is formatted for display.
---

# Internationalization: currency, timezone, locale

Per `docs/PRODUCT_SCOPE.md`, full UI translation is not a v1 commitment, but the
architecture must not make it hard later — these conventions are the cheap part
to get right early.

## Currency

- `Deal.amountCents` (per `crm-data-model`) is paired with a `currency` field
  (ISO 4217 code, e.g. `"USD"`, `"EUR"`) at the `Workspace` level as the
  default, with an optional per-`Deal` override for workspaces that sell in
  multiple currencies. Never assume USD in code — always read the applicable
  currency and format accordingly.
- Store amounts as integer minor units (cents) in the deal's own currency; do
  **not** store a pre-converted "normalized USD" value on the record itself. If
  cross-currency aggregation is needed for a report (`analytics-reporting-expert`),
  convert at query/read-model-refresh time using a rate lookup, and be explicit
  in the UI that aggregated totals are converted estimates, not the literal deal
  values.
- Format currency for display with the browser/server `Intl.NumberFormat` given
  the relevant currency code — never hand-roll `$` + comma formatting, which
  breaks for every non-USD currency.

## Timezone

- Store all timestamps in UTC in the database (Prisma `DateTime` default
  behavior) — never store a "local" wall-clock time.
- Every `User` (or `Workspace`, as a fallback default) has an explicit IANA
  timezone (`"America/New_York"`, not a UTC offset, since offsets shift with
  DST and a fixed offset silently goes wrong twice a year). Convert to the
  viewing user's timezone at render/prompt time using that field, never a
  browser-guessed value alone for anything that's stored or scheduled server-
  side (a scheduled Task's `dueAt` reminder must fire correctly regardless of
  which browser created it).
- AI-drafted content referencing dates/times ("let's meet Tuesday at 2pm")
  must resolve relative to the *recipient's* timezone context where known, not
  the server's or the drafting user's — get this from the Contact/Activity
  context passed into the prompt (`ai-features-architect`).

## Locale-aware formatting

- Dates, numbers, and names format via `Intl` APIs keyed off the viewing user's
  locale (default to a sensible fallback like `en-US` when unset), not
  hardcoded `MM/DD/YYYY`-style formatting — this is what keeps the door open for
  non-US customers without a rewrite.
- Don't assume name order (first/last) is universal in UI copy or AI-generated
  prose beyond what's already modeled (`firstName`/`lastName` on Contact) —
  keep this on the radar for later but don't over-build for it now; the
  concrete near-term requirement is currency and timezone correctness, not a
  full locale-aware name-formatting system.

## What this does not require yet

No translated UI strings, no locale-switcher in v1 — this skill is about not
baking in USD/UTC/en-US assumptions at the data and formatting layer, which is
cheap to avoid now and expensive to retrofit, not about shipping translations
nobody has asked for yet (per `docs/PRODUCT_SCOPE.md`'s scoping discipline).
