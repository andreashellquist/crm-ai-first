using CrmApi.Models;
using CrmApi.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CrmApi.Tests;

[Collection(CrmApiCollection.Name)]
public class NotificationServiceTests(CrmApiFactory factory) : IntegrationTestBase(factory)
{
    private NotificationService Service() => Factory.Services.CreateScope().ServiceProvider.GetRequiredService<NotificationService>();

    [Fact]
    public async Task Notify_WithNoPreferenceRow_CreatesNotification()
    {
        var ws = await SeedWorkspaceAsync();

        await Service().Notify(ws.Workspace.Id, ws.User.Id, "ai_suggestion_ready", "deal", "deal-123");

        var notification = await WithDb(db => db.Notifications.SingleAsync(n => n.WorkspaceId == ws.Workspace.Id));
        Assert.Equal(ws.User.Id, notification.UserId);
        Assert.Equal("ai_suggestion_ready", notification.Type);
        Assert.Equal("deal", notification.EntityType);
        Assert.Equal("deal-123", notification.EntityId);
        Assert.Null(notification.ReadAt);
    }

    [Fact]
    public async Task Notify_WithInAppPreferenceOff_DoesNotCreateNotification()
    {
        var ws = await SeedWorkspaceAsync();
        await WithDb(async db =>
        {
            db.NotificationPreferences.Add(new NotificationPreference { UserId = ws.User.Id, Type = "ai_suggestion_ready", InApp = false });
            await db.SaveChangesAsync();
        });

        await Service().Notify(ws.Workspace.Id, ws.User.Id, "ai_suggestion_ready");

        var count = await WithDb(db => db.Notifications.CountAsync(n => n.WorkspaceId == ws.Workspace.Id));
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Notify_WithInAppPreferenceOn_CreatesNotification()
    {
        var ws = await SeedWorkspaceAsync();
        await WithDb(async db =>
        {
            db.NotificationPreferences.Add(new NotificationPreference { UserId = ws.User.Id, Type = "ai_suggestion_ready", InApp = true });
            await db.SaveChangesAsync();
        });

        await Service().Notify(ws.Workspace.Id, ws.User.Id, "ai_suggestion_ready");

        var count = await WithDb(db => db.Notifications.CountAsync(n => n.WorkspaceId == ws.Workspace.Id));
        Assert.Equal(1, count);
    }
}
