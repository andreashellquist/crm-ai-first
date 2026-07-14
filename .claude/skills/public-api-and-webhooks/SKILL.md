---
name: public-api-and-webhooks
description: Pattern for this CRM's outward-facing developer platform — API key issuance and scoping, versioned REST resource design, and signed/retried outbound webhooks to customer systems. Load this when building the public API or a new outbound webhook event, as opposed to the inbound webhook-handler pattern for third-party providers (see backend-api-engineer) or this app calling out to a provider's API (see integrations-engineer).
---

# Public API & outbound webhooks

Owned by `api-platform-expert`. This is the *outbound* counterpart to
`integrations-engineer` (this app consuming Gmail/Stripe/etc.) and to
`backend-api-engineer`'s inbound webhook-handler pattern (this app receiving
events from providers) — here, this app is the provider.

## API keys

```prisma
model ApiKey {
  id           String    @id @default(cuid())
  workspaceId  String
  name         String
  hashedKey    String    @unique // raw key shown once at creation, never stored
  scopes       String[]          // e.g. ["contacts:read", "deals:write"]
  createdById  String
  lastUsedAt   DateTime?
  revokedAt    DateTime?
  createdAt    DateTime  @default(now())

  @@index([workspaceId])
}
```

- Show the raw key exactly once, at creation — same UX as GitHub/Stripe tokens.
- Authenticate by hashing the presented key and looking up `hashedKey`; check
  `revokedAt` and `scopes` against the requested operation before anything else.
- Requests authenticated by API key resolve to a `workspaceId` exactly like a
  session does, and go through the identical tenant-isolation checks
  (`auth-security-expert`) — there is no separate, less-checked code path for
  API-key auth.

## Resource design

- `/api/v1/...`, versioned in the path; breaking changes ship as `/api/v2/...`
  rather than mutating `v1`'s contract.
- Resource and field names use canonical entity names (`deal`, not a workspace's
  relabeled "Listing") — external integrators need a stable contract; see
  `api-platform-expert` for why terminology relabeling stays a display-layer
  concern.
- `customFields` on a resource is an object keyed by `FieldDefinition.key`; a
  separate `/api/v1/field-definitions?entityType=deal` endpoint lets integrators
  discover what those keys mean for a given workspace.

## Outbound webhooks

```prisma
model WebhookSubscription {
  id          String   @id @default(cuid())
  workspaceId String
  url         String
  secret      String   // used to HMAC-sign delivered payloads
  eventTypes  String[] // e.g. ["deal.stage_changed", "deal.won", "contact.created"]
  isActive    Boolean  @default(true)
  createdAt   DateTime @default(now())
}

model WebhookDelivery {
  id            String   @id @default(cuid())
  subscriptionId String
  eventType     String
  payload       Json
  status        String   // "pending" | "delivered" | "failed"
  attempts      Int      @default(0)
  lastAttemptAt DateTime?
  createdAt     DateTime @default(now())

  @@index([subscriptionId, status])
}
```

- Deliver via the same background-job infrastructure as any other side effect
  (`backend-api-engineer`) — the request/mutation that triggers an event enqueues
  a delivery, it never calls the customer's URL synchronously.
- Sign every payload with the subscription's `secret` (HMAC in a header) so
  receivers can verify it actually came from this app.
- Retry with exponential backoff up to a capped number of attempts/duration;
  record every attempt in `WebhookDelivery` so a workspace admin can see
  delivery history and failure reasons — a subscription failing silently for
  days is a support-ticket-in-waiting, make it visible in the UI instead.
- Emit business events (`deal.won`, `contact.created`, `deal.stage_changed`),
  not raw DB-write events — pick a stable, documented event catalog rather than
  firing a webhook for every internal `UPDATE`.

## Rate limiting

Rate-limit per `ApiKey`, independent of the per-workspace internal rate limits
`backend-api-engineer` applies to the product's own AI/integration usage — an
external integrator hammering the API shouldn't be able to degrade that
workspace's own in-app experience, and vice versa.
