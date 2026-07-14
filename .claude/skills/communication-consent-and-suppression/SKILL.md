---
name: communication-consent-and-suppression
description: Data model and gating pattern for tracking per-contact communication consent (marketing vs transactional, per channel) and enforcing suppression before any send — required before shipping AI-drafted or manual outbound email/SMS, per regional-compliance-expert's CAN-SPAM/CASL/GDPR-ePrivacy/TCPA guidance. Load this when building outbound email/SMS sending, an unsubscribe flow, or any AI feature that can end in a send.
---

# Communication consent & suppression

Owned by `regional-compliance-expert` (what's required, why) and enforced per
`backend-api-engineer`/`ai-features-architect` conventions (where it lives in
the send path). Whatever the regulatory nuance, the engineering requirement is
one rule: **no send path, human or AI, bypasses this check.**

## Data model

```prisma
model ContactConsent {
  id           String    @id @default(cuid())
  workspaceId  String
  contactId    String
  channel      String    // "email" | "sms" | "call"
  purpose      String    // "marketing" | "transactional"
  status       String    // "opted_in" | "opted_out" | "implied" | "unknown"
  source       String?   // "signup_form" | "imported" | "manual" | "reply_unsubscribe"
  consentedAt  DateTime?
  expiresAt    DateTime? // e.g. CASL implied-consent lapse
  updatedAt    DateTime  @updatedAt

  contact Contact @relation(fields: [contactId], references: [id])

  @@unique([contactId, channel, purpose])
  @@index([workspaceId, contactId])
}
```

- `status = "unknown"` is the default for any contact without an explicit
  record. Treat unknown as **not cleared** to send marketing communications —
  fail closed, never open, when consent state is ambiguous.
- Imported contacts (`csv-import-dedupe`) get `source: "imported"`,
  `status: "unknown"` unless the import explicitly captures a consent basis —
  never default an imported list to `opted_in`.
- Model `marketing` and `transactional` separately per contact, not one
  blanket flag: a workspace may be clear to reply to an inbound customer email
  (transactional) while not being clear to send that same contact cold
  marketing outreach.

## Enforcement — one gate, every send path

```ts
async function assertSendAllowed(contactId: string, channel: string, purpose: string) {
  const consent = await db.contactConsent.findUnique({
    where: { contactId_channel_purpose: { contactId, channel, purpose } },
  });
  const allowed = consent?.status === "opted_in" || consent?.status === "implied";
  if (!allowed) throw new SendBlockedError("Contact has not consented to this communication");
  if (consent.expiresAt && consent.expiresAt < new Date()) {
    throw new SendBlockedError("Consent has expired");
  }
}
```

Call this from **one shared send function** used by both AI-drafted sends
(`ai-features-architect`) and manual rep-initiated sends. Never duplicate the
check per call site — a duplicated check is exactly the kind of thing that
drifts and silently stops being enforced the next time a new send path is
added.

## Unsubscribe / opt-out handling

- Every marketing email includes a working unsubscribe mechanism (CAN-SPAM
  requires this regardless of consent basis). Processing an unsubscribe sets
  `status: "opted_out"` immediately — before the next send is possible, not on
  a delay or batch job.
- Inbound "STOP"/"unsubscribe" replies (email or SMS) are parsed and applied
  the same way as a link click — don't require a specific mechanism for an
  opt-out to be honored.
- Opted-out contacts stay opted-out through re-import: `csv-import-dedupe`
  merges must never silently reset `status` back to `unknown`/`opted_in` when
  reconciling an existing contact against imported data.

## Surfacing to the user

Show consent status directly on the contact record and block the compose UI
from offering to send when the gate would reject it, so a rep sees *why*
before attempting — not a runtime failure after the fact. Same "visible, never
silent" principle as `ai-features-architect`'s failure-mode guidance for AI
features generally.
