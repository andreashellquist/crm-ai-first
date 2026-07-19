using CrmApi.Authorization;
using CrmApi.Data;
using CrmApi.Dtos;
using CrmApi.Models;
using CrmApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmApi.Controllers;

// Session-JWT-authenticated management of outbound webhook subscriptions —
// see the public-api-and-webhooks skill. Creating/editing/deleting a
// subscription (which decides what workspace data gets sent to an external
// URL) is owner/admin only; any member can view subscriptions and delivery
// history, same read/write split as ApiKeysController.
[ApiController]
[Route("api/webhook-subscriptions")]
[Authorize]
public class WebhookSubscriptionsController(AppDbContext db, CurrentUser current) : ControllerBase
{
    public static readonly string[] ValidEventTypes =
    [
        "deal.won", "deal.lost", "deal.stage_changed", "contact.created",
    ];

    [HttpGet]
    public async Task<ActionResult<List<WebhookSubscriptionDto>>> List()
    {
        var subscriptions = await db.WebhookSubscriptions
            .Where(s => s.WorkspaceId == current.WorkspaceId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();
        return Ok(subscriptions.Select(ToDto).ToList());
    }

    [HttpPost]
    [RequireRole("owner", "admin")]
    public async Task<ActionResult<CreateWebhookSubscriptionResponse>> Create(CreateWebhookSubscriptionRequest request)
    {
        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
            return BadRequest("Url must be a valid http(s) URL");
        var eventTypes = (request.EventTypes ?? []).Distinct().ToList();
        if (eventTypes.Count == 0) return BadRequest("At least one event type is required");
        var invalid = eventTypes.Except(ValidEventTypes).ToList();
        if (invalid.Count > 0) return BadRequest($"Unknown event type(s): {string.Join(", ", invalid)}");

        var secret = WebhookSigner.GenerateSecret();
        var subscription = new WebhookSubscription
        {
            WorkspaceId = current.WorkspaceId,
            Url = request.Url,
            Secret = secret,
            EventTypes = eventTypes,
        };
        db.WebhookSubscriptions.Add(subscription);
        await db.SaveChangesAsync();

        return Ok(new CreateWebhookSubscriptionResponse(ToDto(subscription), secret));
    }

    [HttpPut("{id}")]
    [RequireRole("owner", "admin")]
    public async Task<ActionResult<WebhookSubscriptionDto>> Update(string id, UpdateWebhookSubscriptionRequest request)
    {
        var subscription = await db.WebhookSubscriptions
            .FirstOrDefaultAsync(s => s.Id == id && s.WorkspaceId == current.WorkspaceId);
        if (subscription is null) return NotFound();

        var eventTypes = (request.EventTypes ?? []).Distinct().ToList();
        if (eventTypes.Count == 0) return BadRequest("At least one event type is required");
        var invalid = eventTypes.Except(ValidEventTypes).ToList();
        if (invalid.Count > 0) return BadRequest($"Unknown event type(s): {string.Join(", ", invalid)}");

        subscription.EventTypes = eventTypes;
        subscription.IsActive = request.IsActive;
        await db.SaveChangesAsync();
        return Ok(ToDto(subscription));
    }

    [HttpDelete("{id}")]
    [RequireRole("owner", "admin")]
    public async Task<IActionResult> Delete(string id)
    {
        var subscription = await db.WebhookSubscriptions
            .FirstOrDefaultAsync(s => s.Id == id && s.WorkspaceId == current.WorkspaceId);
        if (subscription is null) return NotFound();

        db.WebhookSubscriptions.Remove(subscription);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id}/regenerate-secret")]
    [RequireRole("owner", "admin")]
    public async Task<ActionResult<RegenerateWebhookSecretResponse>> RegenerateSecret(string id)
    {
        var subscription = await db.WebhookSubscriptions
            .FirstOrDefaultAsync(s => s.Id == id && s.WorkspaceId == current.WorkspaceId);
        if (subscription is null) return NotFound();

        subscription.Secret = WebhookSigner.GenerateSecret();
        await db.SaveChangesAsync();
        return Ok(new RegenerateWebhookSecretResponse(subscription.Secret));
    }

    [HttpGet("{id}/deliveries")]
    public async Task<ActionResult<List<WebhookDeliveryDto>>> Deliveries(string id)
    {
        var subscription = await db.WebhookSubscriptions
            .FirstOrDefaultAsync(s => s.Id == id && s.WorkspaceId == current.WorkspaceId);
        if (subscription is null) return NotFound();

        var deliveries = await db.WebhookDeliveries
            .Where(d => d.SubscriptionId == id)
            .OrderByDescending(d => d.CreatedAt)
            .Take(50)
            .ToListAsync();
        return Ok(deliveries.Select(d => new WebhookDeliveryDto(d.Id, d.EventType, d.Status, d.Attempts, d.LastAttemptAt, d.CreatedAt)).ToList());
    }

    private static WebhookSubscriptionDto ToDto(WebhookSubscription s) => new(s.Id, s.Url, s.EventTypes, s.IsActive, s.CreatedAt);
}
