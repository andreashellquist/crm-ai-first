---
name: csv-import-dedupe
description: Pattern for CSV contact/company import with deduplication — column mapping, matching strategy (email/domain first, fuzzy name fallback), and merge behavior. Load this when building or changing bulk import, since getting dedup wrong either creates duplicate records that pollute AI context, or silently merges two distinct people/companies.
---

# CSV import & deduplication

## Import flow

1. **Parse & preview**: parse the CSV, let the user map columns to fields
   (email, name, company, phone, custom fields) — never assume column order/
   names match exactly; show a preview of the first ~10 mapped rows before
   committing.
2. **Validate per-row**: Zod-validate each row (email format, required fields);
   collect row-level errors and let the user proceed with valid rows while
   flagging invalid ones, rather than failing the whole import on one bad row.
3. **Dedupe before insert** (see matching strategy below).
4. **Import as a background job** (per `backend-api-engineer`), not inline in the
   request — imports of any real size will exceed a request timeout, and the UI
   should show progress rather than blocking.

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

## On match: merge, don't silently skip or overwrite

- Default to **filling in blanks only** — if the existing Contact already has a
  phone number and the imported row has a different one, don't silently
  overwrite; surface it as a conflict for the user to resolve (or keep existing
  and log the discrepancy, depending on import context — bulk imports typically
  keep-existing, manual re-imports may prefer newest-wins; make this an explicit
  choice in the import UI, not a hardcoded assumption).
- Every merge/update from an import should still go through the same soft-delete/
  audit conventions as any other mutation (per `database-schema-expert`) — an
  import is not a special path that bypasses normal data integrity rules.

## Why this matters for the AI features

Duplicate Contact/Company records directly degrade AI features built on this
data — a summarization or scoring call that only sees half of a contact's
Activities (split across a duplicate) produces a worse and potentially
misleading result. Treat import dedup quality as a correctness requirement for
the AI feature set, not just UI/database hygiene.
