namespace CrmApi.Models;

// A workspace's subscription to this app's outbound business events — see
// the public-api-and-webhooks skill. Secret signs every delivered payload
// (WebhookSigner) so the receiving endpoint can verify authenticity.
public class WebhookSubscription
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string WorkspaceId { get; set; }
    public required string Url { get; set; }
    public required string Secret { get; set; }
    public List<string> EventTypes { get; set; } = [];
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Workspace? Workspace { get; set; }
}

// One row per attempted delivery of one event to one subscription — the
// audit trail a workspace admin needs to see "is this integration actually
// working," not just fire-and-forget. Attempts/LastAttemptAt are updated by
// the deliver_webhook job handler on every try, including retries.
public class WebhookDelivery
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string SubscriptionId { get; set; }
    public required string EventType { get; set; }
    public required string Payload { get; set; } // exact JSON body sent/signed — jsonb text
    public string Status { get; set; } = "pending"; // pending | delivered | failed
    public int Attempts { get; set; }
    public DateTime? LastAttemptAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public WebhookSubscription? Subscription { get; set; }
}
