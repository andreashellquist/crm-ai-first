---
name: notifications-and-digests
description: Pattern for in-app notifications and email digests in this CRM (AI-suggested action ready, deal assigned, mention in an activity, etc.) — per-user preferences, queue-based fanout, and digest batching. Load this when adding a new event that should notify a user, or building the notification center/preferences UI.
---

# Notifications & digests

Cross-cutting between `backend-api-engineer` (delivery/fanout) and
`frontend-engineer` (notification center UI); no dedicated agent since this is
a well-defined pattern rather than an area of ongoing judgment calls.

## Shape

```prisma
model Notification {
  id          String    @id @default(cuid())
  workspaceId String
  userId      String
  type        String    // "deal_assigned" | "ai_suggestion_ready" | "mention" | ...
  entityType  String?   // "deal" | "contact" | ...
  entityId    String?
  readAt      DateTime?
  createdAt   DateTime  @default(now())

  @@index([workspaceId, userId, readAt])
}

model NotificationPreference {
  id          String  @id @default(cuid())
  userId      String
  type        String  // matches Notification.type
  inApp       Boolean @default(true)
  emailDigest String  @default("daily") // "off" | "immediate" | "daily" | "weekly"

  @@unique([userId, type])
}
```

## Fanout

- Notification creation is a side effect of a business event (deal assigned,
  AI suggestion generated, task overdue), enqueued as a background job per
  `backend-api-engineer` — never generated inline in the request that caused it,
  so a burst of activity (e.g. a bulk import) can't slow down the triggering
  action.
- Check `NotificationPreference` before creating an in-app row or queuing an
  email — a user who's turned a type off shouldn't get a `Notification` row at
  all, not just a suppressed one, to keep the notification center accurate.
- AI-generated suggestions/drafts becoming ready is itself a notification-worthy
  event — ties into `ai-features-architect`'s draft-and-review pattern: the
  notification is what tells the rep there's something to review.

## Email digests

- `immediate` sends as soon as the job runs; `daily`/`weekly` batch matching
  notifications into a single scheduled digest email per user rather than
  spamming one email per event — the digest job queries unread/undigested
  `Notification` rows for the period and renders one email.
- Mark notifications as included-in-a-digest (or rely on `readAt`/a separate
  `digestedAt` field) so a digest run doesn't re-include something already sent.
- Respect workspace terminology in notification/email copy ("New Listing
  assigned to you," not "New Deal assigned to you," for a relabeled workspace)
  per `workspace-customization`.

## In-app notification center

Unread count badge, mark-as-read on view or explicit dismissal, and a link that
navigates to the relevant entity — standard pattern, no need to reinvent; keep
it a thin read of the `Notification` table filtered by `userId` +
`workspaceId`, following `frontend-engineer`'s server-component-by-default
conventions with a client component only for the live unread-count badge.
