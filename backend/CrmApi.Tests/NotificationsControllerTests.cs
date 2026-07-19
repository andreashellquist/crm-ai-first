using System.Net;
using System.Net.Http.Json;
using CrmApi.Dtos;
using CrmApi.Models;
using Microsoft.EntityFrameworkCore;

namespace CrmApi.Tests;

[Collection(CrmApiCollection.Name)]
public class NotificationsControllerTests(CrmApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task List_OnlyReturnsCurrentUsersNotifications()
    {
        var ws = await SeedWorkspaceAsync();
        var otherUser = TestData.User();
        var otherMember = TestData.Member(ws.Workspace, otherUser, "member");
        await WithDb(async db =>
        {
            db.Users.Add(otherUser);
            db.WorkspaceMembers.Add(otherMember);
            db.Notifications.AddRange(
                new Notification { WorkspaceId = ws.Workspace.Id, UserId = ws.User.Id, Type = "ai_suggestion_ready" },
                new Notification { WorkspaceId = ws.Workspace.Id, UserId = otherUser.Id, Type = "ai_suggestion_ready" });
            await db.SaveChangesAsync();
        });

        var notifications = await ws.Client.GetFromJsonAsync<List<NotificationDto>>("/api/notifications");

        Assert.NotNull(notifications);
        var notification = Assert.Single(notifications!);
        var persisted = await WithDb(db => db.Notifications.SingleAsync(n => n.Id == notification.Id));
        Assert.Equal(ws.User.Id, persisted.UserId);
    }

    [Fact]
    public async Task List_UnreadOnly_ExcludesReadNotifications()
    {
        var ws = await SeedWorkspaceAsync();
        var read = new Notification { WorkspaceId = ws.Workspace.Id, UserId = ws.User.Id, Type = "ai_suggestion_ready", ReadAt = DateTime.UtcNow };
        var unread = new Notification { WorkspaceId = ws.Workspace.Id, UserId = ws.User.Id, Type = "ai_suggestion_ready" };
        await WithDb(async db => { db.Notifications.AddRange(read, unread); await db.SaveChangesAsync(); });

        var notifications = await ws.Client.GetFromJsonAsync<List<NotificationDto>>("/api/notifications?unreadOnly=true");

        Assert.NotNull(notifications);
        var notification = Assert.Single(notifications!);
        Assert.Equal(unread.Id, notification.Id);
    }

    [Fact]
    public async Task UnreadCount_CountsOnlyUnreadForCurrentUser()
    {
        var ws = await SeedWorkspaceAsync();
        await WithDb(async db =>
        {
            db.Notifications.AddRange(
                new Notification { WorkspaceId = ws.Workspace.Id, UserId = ws.User.Id, Type = "ai_suggestion_ready" },
                new Notification { WorkspaceId = ws.Workspace.Id, UserId = ws.User.Id, Type = "ai_suggestion_ready" },
                new Notification { WorkspaceId = ws.Workspace.Id, UserId = ws.User.Id, Type = "ai_suggestion_ready", ReadAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        });

        var result = await ws.Client.GetFromJsonAsync<UnreadCountResponse>("/api/notifications/unread-count");

        Assert.NotNull(result);
        Assert.Equal(2, result!.Count);
    }

    [Fact]
    public async Task MarkRead_SetsReadAt()
    {
        var ws = await SeedWorkspaceAsync();
        var notification = new Notification { WorkspaceId = ws.Workspace.Id, UserId = ws.User.Id, Type = "ai_suggestion_ready" };
        await WithDb(async db => { db.Notifications.Add(notification); await db.SaveChangesAsync(); });

        var response = await ws.Client.PostAsync($"/api/notifications/{notification.Id}/read", content: null);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var persisted = await WithDb(db => db.Notifications.SingleAsync(n => n.Id == notification.Id));
        Assert.NotNull(persisted.ReadAt);
    }

    [Fact]
    public async Task MarkRead_ForAnotherUsersNotification_ReturnsNotFoundAndDoesNotMarkRead()
    {
        var ws = await SeedWorkspaceAsync();
        var otherUser = TestData.User();
        var otherMember = TestData.Member(ws.Workspace, otherUser, "member");
        var notification = new Notification { WorkspaceId = ws.Workspace.Id, UserId = otherUser.Id, Type = "ai_suggestion_ready" };
        await WithDb(async db =>
        {
            db.Users.Add(otherUser);
            db.WorkspaceMembers.Add(otherMember);
            db.Notifications.Add(notification);
            await db.SaveChangesAsync();
        });

        var response = await ws.Client.PostAsync($"/api/notifications/{notification.Id}/read", content: null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var persisted = await WithDb(db => db.Notifications.SingleAsync(n => n.Id == notification.Id));
        Assert.Null(persisted.ReadAt);
    }

    [Fact]
    public async Task MarkAllRead_MarksAllUnreadForCurrentUserOnly()
    {
        var ws = await SeedWorkspaceAsync();
        var otherUser = TestData.User();
        var otherMember = TestData.Member(ws.Workspace, otherUser, "member");
        var mine1 = new Notification { WorkspaceId = ws.Workspace.Id, UserId = ws.User.Id, Type = "ai_suggestion_ready" };
        var mine2 = new Notification { WorkspaceId = ws.Workspace.Id, UserId = ws.User.Id, Type = "ai_suggestion_ready" };
        var theirs = new Notification { WorkspaceId = ws.Workspace.Id, UserId = otherUser.Id, Type = "ai_suggestion_ready" };
        await WithDb(async db =>
        {
            db.Users.Add(otherUser);
            db.WorkspaceMembers.Add(otherMember);
            db.Notifications.AddRange(mine1, mine2, theirs);
            await db.SaveChangesAsync();
        });

        var response = await ws.Client.PostAsync("/api/notifications/read-all", content: null);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var persisted = await WithDb(db => db.Notifications.ToListAsync());
        Assert.NotNull(persisted.Single(n => n.Id == mine1.Id).ReadAt);
        Assert.NotNull(persisted.Single(n => n.Id == mine2.Id).ReadAt);
        Assert.Null(persisted.Single(n => n.Id == theirs.Id).ReadAt); // untouched
    }
}
