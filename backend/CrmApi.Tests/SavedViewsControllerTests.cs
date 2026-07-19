using System.Net;
using System.Net.Http.Json;
using CrmApi.Dtos;
using CrmApi.Models;
using Microsoft.EntityFrameworkCore;

namespace CrmApi.Tests;

[Collection(CrmApiCollection.Name)]
public class SavedViewsControllerTests(CrmApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Create_PersistsAndReturnsSavedView()
    {
        var ws = await SeedWorkspaceAsync();

        var response = await ws.Client.PostAsJsonAsync("/api/saved-views",
            new CreateSavedViewRequest("contact", "My leads", "lifecycleStage=lead&sort=name"));

        response.EnsureSuccessStatusCode();
        var view = await response.Content.ReadFromJsonAsync<SavedViewDto>();
        Assert.NotNull(view);
        Assert.Equal("My leads", view!.Name);
        Assert.Equal("lifecycleStage=lead&sort=name", view.QueryString);
    }

    [Fact]
    public async Task Create_WithInvalidEntityType_ReturnsBadRequest()
    {
        var ws = await SeedWorkspaceAsync();

        var response = await ws.Client.PostAsJsonAsync("/api/saved-views",
            new CreateSavedViewRequest("not-a-real-entity", "Whatever", "q=x"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task List_OnlyReturnsCurrentUsersViewsForTheGivenEntityType()
    {
        var ws = await SeedWorkspaceAsync();
        var otherUser = TestData.User();
        var otherMember = TestData.Member(ws.Workspace, otherUser, "member");
        await WithDb(async db =>
        {
            db.Users.Add(otherUser);
            db.WorkspaceMembers.Add(otherMember);
            db.SavedViews.AddRange(
                new SavedView { WorkspaceId = ws.Workspace.Id, UserId = ws.User.Id, EntityType = "contact", Name = "Mine", QueryString = "q=a" },
                new SavedView { WorkspaceId = ws.Workspace.Id, UserId = otherUser.Id, EntityType = "contact", Name = "Theirs", QueryString = "q=b" });
            await db.SaveChangesAsync();
        });

        var views = await ws.Client.GetFromJsonAsync<List<SavedViewDto>>("/api/saved-views?entityType=contact");

        Assert.NotNull(views);
        var view = Assert.Single(views!);
        Assert.Equal("Mine", view.Name);
    }

    [Fact]
    public async Task Delete_RemovesOwnSavedView()
    {
        var ws = await SeedWorkspaceAsync();
        var view = new SavedView { WorkspaceId = ws.Workspace.Id, UserId = ws.User.Id, EntityType = "contact", Name = "Temp", QueryString = "q=x" };
        await WithDb(async db => { db.SavedViews.Add(view); await db.SaveChangesAsync(); });

        var response = await ws.Client.DeleteAsync($"/api/saved-views/{view.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var exists = await WithDb(db => db.SavedViews.AnyAsync(v => v.Id == view.Id));
        Assert.False(exists);
    }

    [Fact]
    public async Task Delete_ForAnotherUsersSavedView_ReturnsNotFoundAndDoesNotDelete()
    {
        var ws = await SeedWorkspaceAsync();
        var otherUser = TestData.User();
        var otherMember = TestData.Member(ws.Workspace, otherUser, "member");
        var view = new SavedView { WorkspaceId = ws.Workspace.Id, UserId = otherUser.Id, EntityType = "contact", Name = "Theirs", QueryString = "q=x" };
        await WithDb(async db =>
        {
            db.Users.Add(otherUser);
            db.WorkspaceMembers.Add(otherMember);
            db.SavedViews.Add(view);
            await db.SaveChangesAsync();
        });

        var response = await ws.Client.DeleteAsync($"/api/saved-views/{view.Id}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var exists = await WithDb(db => db.SavedViews.AnyAsync(v => v.Id == view.Id));
        Assert.True(exists);
    }
}
