namespace CrmApi.Dtos;

public record WebhookSubscriptionDto(string Id, string Url, List<string> EventTypes, bool IsActive, DateTime CreatedAt);

public record CreateWebhookSubscriptionRequest(string Url, List<string> EventTypes);

// Secret is only ever present on creation and on an explicit regenerate —
// the workspace admin needs it once to configure their receiving endpoint's
// signature verification, same shown-once posture as an API key's raw value.
public record CreateWebhookSubscriptionResponse(WebhookSubscriptionDto Subscription, string Secret);

public record UpdateWebhookSubscriptionRequest(List<string> EventTypes, bool IsActive);

public record RegenerateWebhookSecretResponse(string Secret);

public record WebhookDeliveryDto(string Id, string EventType, string Status, int Attempts, DateTime? LastAttemptAt, DateTime CreatedAt);
