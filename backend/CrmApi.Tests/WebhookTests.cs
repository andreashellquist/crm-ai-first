using System.Net;
using System.Net.Http.Json;
using CrmApi.Dtos;
using CrmApi.Services;
using Microsoft.EntityFrameworkCore;

namespace CrmApi.Tests;

[Collection(CrmApiCollection.Name)]
public class WebhookTests(CrmApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Create_AsOwner_ReturnsSecretOnceAndPersistsSubscription()
    {
        var ws = await SeedWorkspaceAsync();

        var response = await ws.Client.PostAsJsonAsync("/api/webhook-subscriptions",
            new CreateWebhookSubscriptionRequest("https://example.com/hooks/crm", ["contact.created", "deal.won"]));

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<CreateWebhookSubscriptionResponse>();
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body!.Secret));
        Assert.True(body.Subscription.IsActive);
        Assert.Contains("contact.created", body.Subscription.EventTypes);
    }

    [Fact]
    public async Task Create_InvalidUrl_ReturnsBadRequest()
    {
        var ws = await SeedWorkspaceAsync();
        var response = await ws.Client.PostAsJsonAsync("/api/webhook-subscriptions",
            new CreateWebhookSubscriptionRequest("not-a-url", ["contact.created"]));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_UnknownEventType_ReturnsBadRequest()
    {
        var ws = await SeedWorkspaceAsync();
        var response = await ws.Client.PostAsJsonAsync("/api/webhook-subscriptions",
            new CreateWebhookSubscriptionRequest("https://example.com/hooks", ["not.a.real.event"]));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_AsMember_ReturnsForbidden()
    {
        var ws = await SeedWorkspaceAsync();
        var memberClient = AuthedClient(ws.User.Id, ws.Workspace.Id, "member");
        var response = await memberClient.PostAsJsonAsync("/api/webhook-subscriptions",
            new CreateWebhookSubscriptionRequest("https://example.com/hooks", ["contact.created"]));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task List_OnlyReturnsCallersWorkspaceSubscriptions()
    {
        var owner = await SeedWorkspaceAsync();
        var intruder = await SeedWorkspaceAsync();
        await owner.Client.PostAsJsonAsync("/api/webhook-subscriptions",
            new CreateWebhookSubscriptionRequest("https://example.com/hooks", ["contact.created"]));

        var intruderList = await intruder.Client.GetFromJsonAsync<List<WebhookSubscriptionDto>>("/api/webhook-subscriptions");
        Assert.Empty(intruderList!);
    }

    [Fact]
    public async Task Update_ChangesEventTypesAndActiveFlag()
    {
        var ws = await SeedWorkspaceAsync();
        var create = await ws.Client.PostAsJsonAsync("/api/webhook-subscriptions",
            new CreateWebhookSubscriptionRequest("https://example.com/hooks", ["contact.created"]));
        var created = (await create.Content.ReadFromJsonAsync<CreateWebhookSubscriptionResponse>())!.Subscription;

        var update = await ws.Client.PutAsJsonAsync($"/api/webhook-subscriptions/{created.Id}",
            new UpdateWebhookSubscriptionRequest(["deal.won", "deal.lost"], false));

        update.EnsureSuccessStatusCode();
        var updated = await update.Content.ReadFromJsonAsync<WebhookSubscriptionDto>();
        Assert.False(updated!.IsActive);
        Assert.Contains("deal.won", updated.EventTypes);
        Assert.DoesNotContain("contact.created", updated.EventTypes);
    }

    [Fact]
    public async Task RegenerateSecret_ReturnsANewSecret()
    {
        var ws = await SeedWorkspaceAsync();
        var create = await ws.Client.PostAsJsonAsync("/api/webhook-subscriptions",
            new CreateWebhookSubscriptionRequest("https://example.com/hooks", ["contact.created"]));
        var created = await create.Content.ReadFromJsonAsync<CreateWebhookSubscriptionResponse>();

        var regen = await ws.Client.PostAsync($"/api/webhook-subscriptions/{created!.Subscription.Id}/regenerate-secret", content: null);

        regen.EnsureSuccessStatusCode();
        var regenerated = await regen.Content.ReadFromJsonAsync<RegenerateWebhookSecretResponse>();
        Assert.NotEqual(created.Secret, regenerated!.Secret);
    }

    [Fact]
    public async Task ContactCreated_DeliversSignedPayloadToSubscribedUrl()
    {
        var ws = await SeedWorkspaceAsync();
        // Unique URL per test — Factory.WebhookHandler.Requests accumulates
        // across every test in this collection (one shared factory), so
        // filtering by a test-specific URL is what makes assertions reliable
        // rather than relying on list position/count.
        var url = $"https://example.com/hooks/{ws.Workspace.Id}";
        var subscribe = await ws.Client.PostAsJsonAsync("/api/webhook-subscriptions",
            new CreateWebhookSubscriptionRequest(url, ["contact.created"]));
        var subscription = (await subscribe.Content.ReadFromJsonAsync<CreateWebhookSubscriptionResponse>())!;
        Factory.WebhookHandler.NextStatusCode = HttpStatusCode.OK;

        var createContact = await ws.Client.PostAsJsonAsync("/api/contacts", new { firstName = "Jane" });
        createContact.EnsureSuccessStatusCode();

        await ProcessAllPendingJobsAsync();

        var (request, body) = Assert.Single(Factory.WebhookHandler.Requests, r => r.Request.RequestUri!.ToString() == url);
        Assert.Equal("contact.created", request.Headers.GetValues("X-Crm-Event").Single());
        var expectedSignature = WebhookSigner.Sign(subscription.Secret, body);
        Assert.Equal(expectedSignature, request.Headers.GetValues("X-Crm-Signature").Single());
        Assert.Contains("\"eventType\":\"contact.created\"", body);

        var delivery = await WithDb(db => db.WebhookDeliveries.SingleAsync(d => d.SubscriptionId == subscription.Subscription.Id));
        Assert.Equal("delivered", delivery.Status);
        Assert.Equal(1, delivery.Attempts);
    }

    [Fact]
    public async Task ContactCreated_NoMatchingSubscription_DoesNotEnqueueAnything()
    {
        var ws = await SeedWorkspaceAsync();
        await ws.Client.PostAsJsonAsync("/api/webhook-subscriptions",
            new CreateWebhookSubscriptionRequest("https://example.com/hooks", ["deal.won"])); // not subscribed to contact.created

        var before = Factory.WebhookHandler.Requests.Count;
        var createContact = await ws.Client.PostAsJsonAsync("/api/contacts", new { firstName = "Jane" });
        createContact.EnsureSuccessStatusCode();
        await ProcessAllPendingJobsAsync();

        Assert.Equal(before, Factory.WebhookHandler.Requests.Count);
    }

    [Fact]
    public async Task DealWon_FiresBothStageChangedAndWonEvents()
    {
        var ws = await SeedWorkspaceAsync();
        // TestData.Stage doesn't set IsWon — flip it directly for this test.
        await WithDb(async db =>
        {
            var stage = await db.Stages.SingleAsync(s => s.Id == ws.StageTwo.Id);
            stage.IsWon = true;
            await db.SaveChangesAsync();
        });
        var deal = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne);
        await WithDb(async db => { db.Deals.Add(deal); await db.SaveChangesAsync(); });
        var url = $"https://example.com/hooks/{ws.Workspace.Id}";
        await ws.Client.PostAsJsonAsync("/api/webhook-subscriptions",
            new CreateWebhookSubscriptionRequest(url, ["deal.won", "deal.stage_changed"]));
        Factory.WebhookHandler.NextStatusCode = HttpStatusCode.OK;

        var move = await ws.Client.PostAsJsonAsync($"/api/deals/{deal.Id}/move", new MoveDealRequest(ws.StageTwo.Id));
        move.EnsureSuccessStatusCode();
        await ProcessAllPendingJobsAsync();

        var eventTypes = Factory.WebhookHandler.Requests
            .Where(r => r.Request.RequestUri!.ToString() == url)
            .Select(r => r.Request.Headers.GetValues("X-Crm-Event").Single())
            .ToList();
        Assert.Contains("deal.won", eventTypes);
        Assert.Contains("deal.stage_changed", eventTypes);
    }

    [Fact]
    public async Task Delivery_NonSuccessResponse_StaysPendingForRetry()
    {
        var ws = await SeedWorkspaceAsync();
        var subscribe = await ws.Client.PostAsJsonAsync("/api/webhook-subscriptions",
            new CreateWebhookSubscriptionRequest("https://example.com/hooks", ["contact.created"]));
        var subscriptionId = (await subscribe.Content.ReadFromJsonAsync<CreateWebhookSubscriptionResponse>())!.Subscription.Id;
        Factory.WebhookHandler.NextStatusCode = HttpStatusCode.InternalServerError;

        await ws.Client.PostAsJsonAsync("/api/contacts", new { firstName = "Jane" });
        await ProcessAllPendingJobsAsync();

        var delivery = await WithDb(db => db.WebhookDeliveries.SingleAsync(d => d.SubscriptionId == subscriptionId));
        Assert.Equal("pending", delivery.Status);
        Assert.Equal(1, delivery.Attempts);

        var job = await WithDb(db => db.Jobs.SingleAsync(j => j.Type == "deliver_webhook" && j.WorkspaceId == ws.Workspace.Id));
        Assert.Equal("pending", job.Status);
        Assert.NotNull(job.LastError);
    }

    [Fact]
    public async Task Delivery_ExhaustsMaxAttempts_MarksTerminalFailed()
    {
        var ws = await SeedWorkspaceAsync();
        var subscribe = await ws.Client.PostAsJsonAsync("/api/webhook-subscriptions",
            new CreateWebhookSubscriptionRequest("https://example.com/hooks", ["contact.created"]));
        var subscriptionId = (await subscribe.Content.ReadFromJsonAsync<CreateWebhookSubscriptionResponse>())!.Subscription.Id;
        Factory.WebhookHandler.NextStatusCode = HttpStatusCode.InternalServerError;

        await ws.Client.PostAsJsonAsync("/api/contacts", new { firstName = "Jane" });

        // Force this attempt to be the terminal one instead of waiting out
        // real exponential backoff across 5 real attempts.
        await WithDb(async db =>
        {
            var job = await db.Jobs.SingleAsync(j => j.Type == "deliver_webhook" && j.WorkspaceId == ws.Workspace.Id);
            job.MaxAttempts = 1;
            await db.SaveChangesAsync();
        });
        await ProcessAllPendingJobsAsync();

        var delivery = await WithDb(db => db.WebhookDeliveries.SingleAsync(d => d.SubscriptionId == subscriptionId));
        Assert.Equal("failed", delivery.Status);

        var job = await WithDb(db => db.Jobs.SingleAsync(j => j.Type == "deliver_webhook" && j.WorkspaceId == ws.Workspace.Id));
        Assert.Equal("failed", job.Status);
    }

    [Fact]
    public async Task Deliveries_ReturnsHistoryForThatSubscription()
    {
        var ws = await SeedWorkspaceAsync();
        var subscribe = await ws.Client.PostAsJsonAsync("/api/webhook-subscriptions",
            new CreateWebhookSubscriptionRequest("https://example.com/hooks", ["contact.created"]));
        var subscription = (await subscribe.Content.ReadFromJsonAsync<CreateWebhookSubscriptionResponse>())!.Subscription;
        Factory.WebhookHandler.NextStatusCode = HttpStatusCode.OK;

        await ws.Client.PostAsJsonAsync("/api/contacts", new { firstName = "Jane" });
        await ProcessAllPendingJobsAsync();

        var deliveries = await ws.Client.GetFromJsonAsync<List<WebhookDeliveryDto>>($"/api/webhook-subscriptions/{subscription.Id}/deliveries");
        Assert.Single(deliveries!);
        Assert.Equal("delivered", deliveries![0].Status);
    }
}
