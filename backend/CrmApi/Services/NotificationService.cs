using CrmApi.Data;
using CrmApi.Models;
using Microsoft.EntityFrameworkCore;

namespace CrmApi.Services;

// See the notifications-and-digests skill. Called directly (not enqueued as
// its own job) from places that are already off the request path — e.g.
// JobWorker's next_best_action success handling — per the skill's actual
// intent: don't create notifications inline during synchronous HTTP request
// handling, not "every notification write needs its own queue hop."
public class NotificationService(AppDbContext db)
{
    public async Task Notify(string workspaceId, string userId, string type, string? entityType = null, string? entityId = null)
    {
        // No preference row means "never explicitly configured" — defaults
        // to on, not off, so a brand-new notification type reaches users
        // without requiring them to opt in first.
        var preference = await db.NotificationPreferences
            .FirstOrDefaultAsync(p => p.UserId == userId && p.Type == type);
        if (preference is { InApp: false }) return;

        db.Notifications.Add(new Notification
        {
            WorkspaceId = workspaceId,
            UserId = userId,
            Type = type,
            EntityType = entityType,
            EntityId = entityId,
        });
        await db.SaveChangesAsync();
    }
}
