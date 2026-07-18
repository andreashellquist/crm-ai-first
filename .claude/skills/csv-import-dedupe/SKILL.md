---
name: csv-import-dedupe
description: Pattern for CSV contact/company import with deduplication — column mapping, matching strategy (email/domain first, fuzzy name fallback), and merge behavior. Load this when building or changing bulk import, since getting dedup wrong either creates duplicate records that pollute AI context, or silently merges two distinct people/companies.
---

# CSV import & deduplication

Implemented in `backend/CrmApi/Services/ContactImportService.cs` (contact
import; see `backend/CrmApi.Tests/ContactImportServiceTests.cs` for the
worked matching-priority cases) — treat that as the reference implementation
of everything below, not just this skill's prose.

## Import flow

1. **Parse & preview**: `ContactImportService.Preview` parses the CSV
   (`CsvParser.cs` — a small hand-written RFC-4180-ish parser, no external
   dependency) and returns headers + the first 10 rows, synchronously (fast
   enough not to need the job queue). `POST /api/contacts/import/preview`.
   The frontend (`contacts/import/import-form.tsx`) lets the user map columns
   to fields, with best-effort auto-detection off common header name aliases
   ("Email"/"E-mail" → `email`, "First Name"/"Given Name" → `firstName`, etc.)
   — never assume column order/names match exactly.
2. **Validate per-row**: a simple regex checks email format; a row with
   nothing usable to match or create on (no email, no first/last name) is
   counted as **skipped**, not an error — blank rows are common in real
   exports and aren't a problem worth surfacing. A row with a real problem
   (invalid email) is counted as an **error** with a 1-based CSV line number
   and message, and is not imported — one bad row never fails the whole
   import.
3. **Dedupe before insert** (see matching strategy below).
4. **Import as a background job** (`import_contacts` job type, per
   `backend-api-engineer`), not inline in the request —
   `POST /api/contacts/import` enqueues and returns a job id immediately; the
   frontend polls the same `/api/jobs/{jobId}` endpoint every AI feature
   polls, and renders a created/updated/skipped/errors summary on completion.

**v1 scope note**: mapping is fixed to `email`/`firstName`/`lastName`/`phone`/
`companyName`/`companyDomain` — custom-field mapping is a deliberate
follow-up (`CustomFieldValidator` is entity-type-agnostic, so wiring it into
`ContactImportService` later is additive), not an oversight.

## Matching strategy

Applied in this priority order, first match wins:

1. **Exact email match** (Contact) — the strongest signal; case-insensitive,
   trimmed.
2. **Domain + exact name match** (Contact within a Company) — for rows missing
   email but with a company domain and a name close enough to an existing
   contact at that company.
3. **Company domain match** (Company) — `acme.com` vs `www.acme.com` normalize to
   the same domain; don't match on company *name* alone (too many false
   positives — "Acme" vs "Acme Inc" vs an unrelated "Acme Plumbing").

Do not implement fuzzy name-only matching across the whole contact base — the
false-positive rate (merging two different people who happen to share a name) is
worse than the false-negative rate (an occasional avoidable duplicate a user can
merge manually later).

Resolving/creating the `Company` a `Contact` attaches to follows the same
domain-first priority: a row's explicit `companyDomain` column wins; failing
that, a domain is derived from the row's email address (text after `@`);
failing that, match falls back to exact company name. `ContactImportService`
loads the whole workspace's existing Contacts/Companies into memory once per
import and matches/creates against those in-memory dictionaries rather than
querying per row — a newly-added-but-unsaved `Company` isn't visible to a
fresh LINQ query within the same `SaveChangesAsync` batch, and per-row round
trips don't scale. This is a deliberate v1 tradeoff for this app's SMB-scale
target (`docs/PRODUCT_SCOPE.md`: 5-500 seats per workspace) — a workspace
with an unusually large existing contact base would want this changed to
indexed per-row lookups, and there's a `MaxRows` cap (10,000) as a guardrail
in the meantime.

## On match: merge, don't silently skip or overwrite

- **Fill in blanks only** — `ContactImportService.ApplyFillBlanksOnly` only
  ever writes a field that is currently null/empty on the existing Contact;
  if the existing Contact already has a phone number and the imported row has
  a different one, the existing value wins and the row's value is discarded,
  not overwritten. There is no newest-wins mode yet — if a workspace needs
  that (e.g. a deliberate re-import meant to refresh stale data), it's an
  explicit new import mode to design, not a silent default to change.
- Every merge/update from an import goes through the same soft-delete
  (`DeletedAt`) conventions as any other Contact mutation (per
  `database-schema-expert`) — an import is not a special path that bypasses
  normal data integrity rules.

## Why this matters for the AI features

Duplicate Contact/Company records directly degrade AI features built on this
data — a summarization or scoring call that only sees half of a contact's
Activities (split across a duplicate) produces a worse and potentially
misleading result. Treat import dedup quality as a correctness requirement for
the AI feature set, not just UI/database hygiene.
