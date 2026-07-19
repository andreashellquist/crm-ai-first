---
name: notifications-and-digests
description: Pattern for in-app notifications and email digests in this CRM (AI-suggested action ready, deal assigned, mention in an activity, etc.) — per-user preferences, queue-based fanout, and digest batching. Load this when adding a new event that should notify a user, or building the notification center/preferences UI.
---

# Notifications & digests

Cross-cutting between `backend-api-engineer` (delivery/fanout) and
`frontend-engineer` (notification center UI); no dedicated agent since this is
a well-defined pattern rather than an area of ongoing judgment calls.

Implemented in `backend/CrmApi/Services/NotificationService.cs` and
`Controllers/NotificationsController.cs`, with the notification center UI in
`src/app/(app)/notification-bell.tsx` — treat those as the reference
implementation of everything below.

## Shape

```csharp
// Models/Notification.cs
public class Notification
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string WorkspaceId { get; set; }
    public required string UserId { get; set; }
    public required string Type { get; set; } // "ai_suggestion_ready" | "task_overdue" | ...
    public string? EntityType { get; set; } // "deal" | "contact" | ...
    public string? EntityId { get; set; }
    public DateTime? ReadAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// Models/NotificationPreference.cs — per-user, not per-workspace. This app
// has no workspace-switcher yet (a session resolves to a single membership
// at login), so that's not a real gap today; revisit if multi-workspace
// switching ships.
public class NotificationPreference
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string UserId { get; set; }
    public required string Type { get; set; } // matches Notification.Type
    public bool InApp { get; set; } = true;
    public string EmailDigest { get; set; } = "daily"; // "off" | "immediate" | "daily" | "weekly"
}
```

No preference row for a `(UserId, Type)` pair means "never explicitly
configured" and defaults to **on** (`NotificationService.Notify`) — a new
notification type reaches users without requiring them to opt in first.

## Fanout

- Notification creation is a side effect of a business event, called directly
  from wherever that event is already off the request path — e.g.
  `JobWorker`'s `next_best_action` success handler calls
  `NotificationService.Notify(...)` right after the job succeeds. This does
  **not** mean every notification write needs its own queue hop: the skill's
  actual intent is "don't create notifications inline during synchronous HTTP
  request handling," and a background job's success handler is already off
  that path. A future trigger that fires from a controller action *would*
  need to go through the job queue first.
- Check `NotificationPreference` before creating an in-app row — a user who's
  turned a type off shouldn't get a `Notification` row at all, not just a
  suppressed one, to keep the notification center accurate.
- AI-generated suggestions/drafts becoming ready is itself a notification-worthy
  event — ties into `ai-features-architect`'s draft-and-review pattern: the
  notification is what tells the rep there's something to review. This is
  the one trigger wired today (`ai_suggestion_ready`, fired when
  `next_best_action` succeeds — see `Job.RequestedByUserId`, set when
  `PipelineController.NextBestAction` enqueues, for who gets notified).
- **`deal_assigned` and `task_overdue` are not wired yet, on purpose.**
  Neither `Deal` nor `TaskItem` has an assignee/owner column in this schema,
  so "who gets notified" has no real answer today — notify every workspace
  member? invent an assignee field? Both are `crm-domain-expert`-relevant
  product decisions, not something to guess at while wiring a notification
  trigger. Add task/deal assignment first, then wire these triggers the same
  way `ai_suggestion_ready` is wired.

## Email digests

- `immediate` sends as soon as the job runs; `daily`/`weekly` batch matching
  notifications into a single scheduled digest email per user rather than
  spamming one email per event — the digest job queries unread/undigested
  `Notification` rows for the period and renders one email.
- Mark notifications as included-in-a-digest (or rely on `ReadAt`/a separate
  `DigestedAt` field) so a digest run doesn't re-include something already sent.
- Respect workspace terminology in notification/email copy ("New Listing
  assigned to you," not "New Deal assigned to you," for a relabeled workspace)
  per `workspace-customization`.
- **Not implemented.** `NotificationPreference.EmailDigest` exists and is
  stored, but nothing reads it — there is no email-sending capability
  anywhere in this app yet (see CLAUDE.md's "notably not yet built"). Build
  actual email sending before building a digest job to sit on top of it.

## In-app notification center

Unread count badge, mark-as-read on view or explicit dismissal, and a link that
navigates to the relevant entity — standard pattern, no need to reinvent.
`NotificationBell` (client component, in the `(app)` layout header) polls
`GET /api/notifications/unread-count` every 20s for the badge, and only
fetches the full list (`GET /api/notifications`) when the panel is opened —
not on every poll tick, to avoid an unnecessary request every 20 seconds for
data the user isn't looking at. `POST /api/notifications/{id}/read` and
`POST /api/notifications/read-all` both re-scope by `current.UserId` as well
as `current.WorkspaceId` — notifications are personal, not just
workspace-scoped, so a teammate in the same workspace must never be able to
read or mark another user's notifications (see
`MultiTenantIsolationTests.Notifications_List_OnlyReturnsCallersWorkspace`
and `NotificationsControllerTests`' same-workspace-different-user cases for
the regression guards — this is a distinct isolation axis from the usual
cross-workspace check, easy to miss if you only test workspace boundaries).
