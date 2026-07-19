using System.Text.Json;
using CrmApi.Data;
using CrmApi.Models;
using Microsoft.EntityFrameworkCore;

namespace CrmApi.Services;

// Fans a business event out to every active, matching WebhookSubscription in
// the workspace — one WebhookDelivery row + one deliver_webhook job per
// subscription, never a synchronous HTTP call from the request that
// triggered the event (public-api-and-webhooks skill). Deliberately a small,
// fixed event catalog (deal.won, deal.lost, deal.stage_changed,
// contact.created), not a webhook fired for every internal write.
public class WebhookDeliveryService(AppDbContext db, JobQueueService queue)
{
    private const int MaxDeliveryAttempts = 5;
    private static readonly JsonSerializerOptions PayloadOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task Enqueue(string workspaceId, string eventType, object data)
    {
        var subscriptions = await db.WebhookSubscriptions
            .Where(s => s.WorkspaceId == workspaceId && s.IsActive)
            .ToListAsync();
        var matching = subscriptions.Where(s => s.EventTypes.Contains(eventType)).ToList();
        if (matching.Count == 0) return;

        var payloadJson = JsonSerializer.Serialize(
            new { eventType, data, timestamp = DateTime.UtcNow }, PayloadOptions);

        foreach (var subscription in matching)
        {
            var delivery = new WebhookDelivery
            {
                SubscriptionId = subscription.Id,
                EventType = eventType,
                Payload = payloadJson,
            };
            db.WebhookDeliveries.Add(delivery);
            await db.SaveChangesAsync();
            await queue.Enqueue("deliver_webhook", new { deliveryId = delivery.Id }, workspaceId, maxAttempts: MaxDeliveryAttempts);
        }
    }
}
