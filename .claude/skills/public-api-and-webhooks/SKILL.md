---
name: public-api-and-webhooks
description: Pattern for this CRM's outward-facing developer platform — API key issuance and scoping, versioned REST resource design, and signed/retried outbound webhooks to customer systems. Load this when building the public API or a new outbound webhook event, as opposed to the inbound webhook-handler pattern for third-party providers (see backend-api-engineer) or this app calling out to a provider's API (see integrations-engineer).
---

# Public API & outbound webhooks

Owned by `api-platform-expert`. This is the *outbound* counterpart to
`integrations-engineer` (this app consuming Gmail/Stripe/etc.) and to
`backend-api-engineer`'s inbound webhook-handler pattern (this app receiving
events from providers) — here, this app is the provider.

**Built**: API keys + scoped v1 read endpoints + outbound webhooks — see
`Models/ApiKey.cs`, `Authorization/ApiKeyAuthenticationHandler.cs`,
`Authorization/RequireScopeAttribute.cs`, `Controllers/ApiKeysController.cs`,
`Controllers/V1/*`, `Models/Webhook.cs`, `Services/WebhookDeliveryService.cs`,
`Services/WebhookSigner.cs`, `Controllers/WebhookSubscriptionsController.cs`
in `backend/CrmApi` — treat those as the reference implementation of
everything below. Frontend at `src/app/(app)/settings` (API keys + webhooks
sections).

## API keys

```csharp
public class ApiKey
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string WorkspaceId { get; set; }
    public required string Name { get; set; }
    public required string HashedKey { get; set; } // unique — SHA-256 hex of the raw key
    public List<string> Scopes { get; set; } = []; // e.g. ["contacts:read", "deals:read"]
    public required string CreatedByUserId { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

- `ApiKeyGenerator.Generate()` returns `(rawKey, hashedKey)` — a
  `crm_live_`-prefixed random token and its SHA-256 hash. Only the hash is
  ever persisted; `ApiKeysController.Create` returns the raw key exactly
  once, same UX as GitHub/Stripe tokens. A fast hash (not bcrypt) is
  deliberate here — an API key is already a high-entropy random secret, not
  a low-entropy human-chosen password bcrypt's slowness is meant to blunt.
- `ApiKeyAuthenticationHandler` is a second ASP.NET Core authentication
  scheme (`X-Api-Key` header, not the session JWT/`Authorization: Bearer`)
  that hashes the presented key, looks up `HashedKey`, rejects a missing/
  revoked key, and builds a `ClaimsPrincipal` with the same `"workspaceId"`
  claim session auth uses — so `CurrentUser.WorkspaceId` resolves identically
  regardless of which scheme authenticated the request, and every `/api/v1`
  controller re-scopes its queries by it exactly like the rest of the app.
  Scopes become one `"scope"` claim per entry, checked by
  `RequireScopeAttribute` (the scope-based counterpart to
  `RequireRoleAttribute`).
- `ApiKeysController` (session-JWT-authenticated, `[RequireRole("owner",
  "admin")]` to create/revoke — any member can list) is where a workspace
  manages its own keys.
- **This same credential/scoping infrastructure is reused for SCIM**: a key
  carrying the `scim:users` scope authenticates an identity provider's SCIM
  2.0 connector at `Controllers/ScimUsersController.cs`
  (`/api/scim/v2/Users`) — deliberately not a parallel secret system. See
  `auth-security-expert`'s "Enterprise auth" section for the SCIM
  provisioning/deprovisioning details and its one real limitation (no
  server-side session store to revoke an already-issued JWT against).

## Resource design

- `/api/v1/...` (`Controllers/V1/`), versioned in the path; breaking changes
  would ship as `/api/v2/...` rather than mutating `v1`'s contract.
- Resource DTOs (`Dtos/PublicApiDtos.cs` — `PublicContactDto`,
  `PublicCompanyDto`, `PublicDealDto`) use canonical entity field names, not
  a workspace's relabeled terminology — external integrators need a stable
  contract; terminology relabeling stays a display-layer concern for this
  app's own UI (`workspace-customization`).
- `customFields` on a resource is an object keyed by `FieldDefinition.Key`;
  `GET /api/v1/field-definitions?entityType=deal` lets integrators discover
  what those keys mean for a given workspace (reuses `FieldDefinitionDto`).
- **v1 scope, deliberate**: read-only (`{entity}:read` scopes) and capped at
  100 results per list call (`?limit=`, no cursor pagination yet) — covers
  the common "pull my CRM data into another system" integration shape.
  Write scopes and real pagination are a follow-up once there's a concrete
  integration that needs them, not a hidden gap.

## Outbound webhooks

```csharp
public class WebhookSubscription
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string WorkspaceId { get; set; }
    public required string Url { get; set; }
    public required string Secret { get; set; } // HMACs every delivered payload
    public List<string> EventTypes { get; set; } = []; // e.g. ["deal.won", "contact.created"]
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class WebhookDelivery
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string SubscriptionId { get; set; }
    public required string EventType { get; set; }
    public required string Payload { get; set; } // exact JSON body sent/signed
    public string Status { get; set; } = "pending"; // pending | delivered | failed
    public int Attempts { get; set; }
    public DateTime? LastAttemptAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

- `WebhookDeliveryService.Enqueue(workspaceId, eventType, data)` fans an
  event out to every active, matching `WebhookSubscription` — one
  `WebhookDelivery` row + one `deliver_webhook` job per subscription, never a
  synchronous HTTP call from the request/mutation that triggered the event
  (`backend-api-engineer`'s background-job convention). Called from
  `PipelineController.MoveDeal` (`deal.stage_changed`, plus `deal.won`/
  `deal.lost` when the target `Stage.IsWon`/`IsLost`) and
  `ContactsController.Create` (`contact.created`) — a small, fixed event
  catalog, not a webhook fired for every internal write.
- `WebhookSigner.Sign(secret, payload)` HMAC-SHA256s the exact JSON string
  that gets sent, as an `X-Crm-Signature: sha256=<hex>` header (plus
  `X-Crm-Event: <type>`) — a receiver recomputes the same HMAC over the raw
  body to verify the payload actually came from this app.
- **Retry/backoff deliberately reuses `JobWorker`'s existing mechanism**
  rather than a bespoke retry loop: the `deliver_webhook` handler
  (`JobWorker.cs`) throws on a non-2xx response or network failure, and the
  worker's standard `Job.Attempts`/`MaxAttempts` exponential backoff (capped
  at 60s between tries) takes it from there. The handler compares
  `job.Attempts >= job.MaxAttempts` (already reflecting the current attempt,
  since the claiming `UPDATE` increments it before the handler runs) to know
  whether this was the terminal try, and marks `WebhookDelivery.Status`
  `"failed"` only then — otherwise it stays `"pending"` for the next retry.
  `WebhookSubscriptionsController`'s `GET /{id}/deliveries` (last 50, newest
  first) is the visibility a workspace admin needs to see a subscription is
  actually working, or has been silently failing.

## Rate limiting

`Program.cs` registers an ASP.NET Core `RateLimiter` fixed-window policy
(`"ApiKey"`, 100 requests/minute, partitioned by the authenticated
`ApiKey.Id`) applied via `[EnableRateLimiting("ApiKey")]` on every
`Controllers/V1` controller — independent of any internal per-workspace
limits this app applies to its own AI/integration usage, so a runaway
external integration can't degrade that workspace's own in-app experience
and vice versa.
