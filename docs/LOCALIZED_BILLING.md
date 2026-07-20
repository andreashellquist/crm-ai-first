# Localized billing & tax — requirements for a future Phase 2 billing build

**Status: doubly blocked, not started.** This document is written against a
billing system that does not exist yet. Confirmed by grepping the codebase
(`backend/CrmApi`, `src/`) for `Stripe`, `subscription`, `invoice`, `VAT`: zero
hits outside of planning docs (`docs/PRODUCT_SCOPE.md`,
`.claude/agents/integrations-engineer.md`,
`.claude/agents/regional-compliance-expert.md`). There is no Stripe
integration, no plan/subscription model, no invoice generation, no billing
address or tax-ID field anywhere in `Models/Workspace.cs` or elsewhere. Phase
2's "Billing & plans" scope row (`docs/PRODUCT_SCOPE.md` §3, owned by
`integrations-engineer`) — Stripe subscriptions, usage-based limits — has
never been built.

This document is therefore **not** a spec for localized billing as a
buildable feature right now. It's the requirements a *future* Phase 2 billing
implementation needs to get right on tax/localization from day one, written
down now so that work doesn't get built USD/US-only and then need a costly
retrofit — the same "cheap now, expensive to retrofit" framing
`i18n-currency-timezone` uses for currency/timezone, applied here to tax. Per
this agent's charter (`.claude/agents/regional-compliance-expert.md`), this
identifies requirements and where engineering guardrails must sit; it does
not implement them, and **nothing in this document is legal or tax advice**.
Tax treatment (what must be charged, collected, and remitted where) requires
real accounting/legal counsel before any of this ships — this document is
scoped to the engineering question of what data model and integration shape
avoids painting that future work into a corner.

## 0. Relationship to what's already built

`Workspace.DefaultCurrency` (`backend/CrmApi/Models/Workspace.cs`) and the
`i18n-currency-timezone` skill solve *currency display* — what symbol/format a
deal amount renders in. That is a solved, adjacent, but genuinely distinct
problem from what this document covers. Currency formatting answers "is this
$100 or €100 or ¥100"; VAT/GST and e-invoicing answer "does this transaction
need tax added, does the buyer need to self-account for it, and does the
resulting invoice need to be a specific structured document filed with a tax
authority." A workspace can have its currency display fully correct (already
true today) and still be completely unaddressed on tax — these are not the
same checkbox.

## 1. VAT/GST display

### 1.1 Inclusive vs. exclusive pricing display

Whether a displayed price must include tax depends on who's buying and where,
not just where the seller is:

- **B2C in most VAT/GST jurisdictions (EU, UK, Australia, etc.)** — consumer
  protection law generally requires prices shown to a consumer to be
  **tax-inclusive**. This product is explicitly B2B (`docs/PRODUCT_SCOPE.md`
  §1: "Buyer: B2B, sales-led... not consumer/B2C"), so this case is unlikely
  to bind directly on CRM subscription pricing sold to a business — but note
  it now in case pricing is ever shown to an individual (e.g. a solo
  freelancer signing up as a "business" in name only is still often treated
  as a consumer under EU consumer law).
- **B2B transactions** — commonly shown **tax-exclusive** with tax calculated
  and itemized at checkout/invoice time, since a VAT-registered business buyer
  typically reclaims or self-accounts for the tax rather than bearing it as a
  final cost. This is the default case for this product's actual buyer
  profile.
- Concretely: whatever future pricing/checkout page gets built should display
  a base price plus a clearly itemized tax line at the point tax is
  calculated (not baked silently into one number), and that calculation must
  be region-aware — a flat "add 20% VAT to everyone" is wrong for non-EU
  buyers and for exempt EU B2B buyers (§1.2).

### 1.2 Reverse-charge mechanism (EU B2B)

For a VAT-registered EU business buying from a seller in a different EU
member state (or from outside the EU), the standard mechanism is
**reverse-charge**: the seller does not charge VAT; the buyer self-assesses
and reports it on their own VAT return. This is the mechanism that makes VAT
number capture load-bearing rather than decorative:

- A valid, verified EU VAT number on the buyer's account is what
  qualifies a sale for reverse-charge treatment (0% VAT charged by the
  seller). Without one, the seller is generally required to charge VAT at the
  buyer's local rate.
- VAT number **format** validation (regex per country prefix) is necessary
  but not sufficient — a syntactically valid-looking number can still be
  fake, expired, or belong to a different business. Real verification means
  checking against the EU's VIES registry (VAT Information Exchange System)
  or an equivalent service, not just a regex. This is exactly the kind of
  calculation/verification work a tax platform (Stripe Tax, or an equivalent)
  should own rather than this app re-implementing VIES lookups — see §3.
- **Where this needs to be captured**: at signup/billing-setup time for an EU
  workspace (or a "billing country" field set to an EU member state), not
  buried in a later settings page nobody visits — reverse-charge eligibility
  needs to be known *before* the first invoice is generated, since retroactively
  reclassifying already-issued invoices is exactly the kind of rework this
  document exists to avoid.

### 1.3 When a VAT number must be captured at all

Not every workspace needs a VAT number field to be meaningful — only
workspaces billing from a VAT/GST jurisdiction where B2B reverse-charge or
input-tax-credit mechanisms apply (EU, UK, and a growing list of GST
jurisdictions like Australia, Canada (GST/HST), Singapore, India). A US-only
customer base doesn't need this field to do anything (US sales tax works
differently — nexus-based, no VAT-number/reverse-charge concept). The field
should exist unconditionally on the billing data model (cheap, see §3) but
the *logic* that acts on it (reverse-charge calculation, VIES validation)
only needs to activate for billing countries where it's applicable — another
instance of this repo's "configuration over forking" principle
(`workspace-customization` skill) applied to tax rules instead of vertical
features: a `BillingCountry` field plus a tax-calculation provider that knows
the region-specific rules, not `if (country == "DE") { ... }` branches
scattered through checkout code.

## 2. E-invoicing mandates

E-invoicing means the invoice isn't just a PDF emailed to the customer — it's
a structured, machine-readable document (typically XML or a signed schema)
submitted to or cleared through a government platform as part of issuing it
legally. This is a materially different integration shape than "generate a
nice-looking PDF," and it's expanding, not shrinking. Scoped here to the
regimes actually plausible for this product's likely customer base (B2B SaaS,
SMB-to-mid-market, per `docs/PRODUCT_SCOPE.md` §1) — not an exhaustive global
survey:

- **Italy (SDI — Sistema di Interscambio)** — already mandatory for
  essentially all B2B/B2C invoicing between Italian VAT-registered entities.
  If a workspace with an Italian billing address and Italian customers is
  ever real, invoices to and from it likely need to route through SDI in the
  FatturaPA XML format, not just be emailed as a PDF. This is not a "coming
  someday" item for Italy — it's the current baseline there.
- **EU-wide (ViDA — VAT in the Digital Age)** — an evolving EU package
  intended to standardize and eventually mandate e-invoicing/digital
  reporting for cross-border (and increasingly domestic) B2B transactions
  across member states, phasing in over the back half of this decade.
  Concretely: assume more EU countries add mandates similar to Italy's over
  the life of this product, not that Italy is a one-off. Treat "which EU
  countries currently require it" as something to check against current
  guidance at implementation time, not a fixed list to hardcode — this is
  actively moving.
- **Mexico (CFDI — Comprobante Fiscal Digital por Internet)** — mandatory,
  government-cleared structured e-invoicing for essentially all invoicing
  in Mexico, including B2B SaaS. Relevant the moment there's a
  Mexico-domiciled paying workspace, not a "large enterprise only" concern.
- **India (e-invoicing under GST)** — mandatory structured e-invoicing
  (reported to the Invoice Registration Portal, IRP) for businesses above a
  turnover threshold that has been lowered over time to cover more of the
  SMB segment. Relevant given India's large B2B SaaS buyer base.
- **Not currently in scope to track closely**: economies without a
  near-term B2B SaaS mandate relevant to this product's buyer profile
  (most of Africa, most of Southeast Asia outside Singapore/India context
  above). Revisit if a specific deal in a specific country raises it —
  per this agent's "don't block on exhaustive research" principle, this list
  should grow when a real customer/region demands it, not preemptively.

### Integration shape implied

None of the above should be hand-rolled. The concrete implication for whoever
builds Phase 2 billing: e-invoicing compliance is a job for a
**compliant invoice-generation format/provider** — either (a) Stripe
Invoicing combined with Stripe Tax's e-invoicing support where it covers the
relevant region, or (b) a dedicated e-invoicing compliance vendor
(the kind of provider that specializes in FatturaPA/CFDI/IRP integration)
sitting behind the billing system, not a bespoke XML generator per country
built in-house. This is the same "use the platform, don't reinvent it"
posture the regional-compliance-expert charter takes toward Stripe Tax for
calculation (§3 of that agent's charter) — extended here to invoice format
and government submission specifically. **Action item for whoever picks up
Phase 2 billing**: before building invoice generation, check which
`integrations-engineer`-selected billing provider's e-invoicing support
actually covers the regions this product's customer base is in at that
time — do not assume Stripe's default invoice PDF satisfies any of the three
regimes above; it does not clear through SDI/CFDI/IRP on its own as of this
writing and needs either Stripe's specific tax/invoicing add-ons or a
separate compliance vendor in the loop.

## 3. Guidance for whoever builds Phase 2 billing: what to add now vs. defer

The billing feature itself is out of scope for this document — this section
is only about not shipping Phase 2 billing with a data model that has to be
migrated painfully later. Cheap-now items are genuinely cheap (a handful of
nullable columns); the corresponding compliance *logic* is correctly deferred
until there's a real customer in a regime that needs it, per this agent's
"don't block on speculative research" principle.

**Add to the billing/workspace data model on day one (cheap, low-regret):**

- `BillingCountry` (ISO 3166-1 alpha-2) — distinct from any existing
  workspace locale/currency field; this is "where is the paying entity
  domiciled for tax purposes," which is not reliably inferable from
  `Workspace.DefaultCurrency` (a workspace could bill in USD from Germany) or
  from a member's `Timezone` (per-user, not per-workspace, and not
  necessarily where the legal entity sits).
- `VatNumber` / `TaxId` (nullable string) — the raw captured value; validate
  format at minimum, but do not treat unvalidated format as proof of a valid
  reverse-charge exemption (§1.2) — store a separate
  `VatNumberValidatedAt`/`VatNumberValid` flag once real VIES (or equivalent)
  verification is wired up, rather than conflating "captured" with
  "verified."
- `BillingLegalName` and a structured `BillingAddress` (street/city/
  region/postal/country) — most e-invoicing and VAT-invoice-format
  requirements need a full legal name and address distinct from the
  workspace's display `Name`, which may be a product/brand name rather than
  the legal entity.
- `TaxExempt` (boolean, nullable/tri-state — "unknown" must not default to
  "exempt") — a hook for nonprofit/reseller/government exemptions that will
  come up eventually; cheap to add as a column, expensive to bolt onto a
  billing engine already live with real invoices.

**Genuinely defer until a specific customer/region requires it:**

- Any actual VAT-rate calculation logic — this is what Stripe Tax (or
  equivalent) exists for; do not hand-roll country/region tax-rate tables.
- VIES (or other real-time VAT registry) verification integration — build it
  when the first EU B2B customer signs up and reverse-charge eligibility is
  a real transaction, not preemptively.
- Any e-invoicing/government-clearance integration (SDI, CFDI, IRP) — these
  are per-country, non-trivial integrations; build the specific one a real
  customer's jurisdiction requires, not a generic framework for all of them
  speculatively. The data model fields above (`BillingCountry`,
  `BillingLegalName`, `BillingAddress`, `VatNumber`) are exactly the fields
  these integrations will need already populated — that's the entire point
  of adding them early.
- Inclusive-vs-exclusive display branching (§1.1) beyond a single itemized
  tax line at checkout — full B2C consumer-pricing-law handling isn't needed
  given this product's stated B2B buyer profile (`docs/PRODUCT_SCOPE.md` §1);
  revisit only if that assumption changes.

## 4. What not to do

- Don't build any part of this now. There is no billing system for it to
  attach to — building tax logic ahead of the billing system it belongs to
  is speculative work with nothing to validate it against, the same failure
  mode this repo's other planning docs warn against ("don't block shipping...
  waiting for exhaustive research," applied in reverse: don't build ahead of
  a real need either).
- Don't assume Stripe Tax (or whichever provider Phase 2 billing picks)
  silently satisfies invoice *format*/e-invoicing mandates just because it
  handles *calculation* — confirm current provider coverage for the specific
  regions in play at implementation time (§2).
- Don't hardcode region logic into `if (country == "IT")`-shaped branches in
  application code — express it as data on the billing/workspace model
  (`BillingCountry`, `VatNumber`, `TaxExempt`) that a generic tax/invoicing
  provider integration reads, matching this repo's configuration-over-forking
  convention used elsewhere (`workspace-customization`,
  `communication-consent-and-suppression`).
- Don't present any of this as compliance sign-off. This document identifies
  engineering requirements and where guardrails must sit; actual tax
  treatment and invoicing obligations require real accounting/legal counsel
  before Phase 2 billing ships into any specific region.
