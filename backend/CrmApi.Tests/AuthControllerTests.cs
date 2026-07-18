using System.Net;
using System.Net.Http.Json;
using CrmApi.Dtos;
using CrmApi.Services;
using Microsoft.EntityFrameworkCore;

namespace CrmApi.Tests;

[Collection(CrmApiCollection.Name)]
public class AuthControllerTests(CrmApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Login_WithValidCredentials_ReturnsTokenAndWorkspace()
    {
        var workspace = TestData.Workspace();
        var user = TestData.User(password: "correct-password");
        var member = TestData.Member(workspace, user, "owner");
        await WithDb(async db =>
        {
            db.Workspaces.Add(workspace);
            db.Users.Add(user);
            db.WorkspaceMembers.Add(member);
            await db.SaveChangesAsync();
        });

        var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email, "correct-password"));

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body!.Token));
        Assert.Equal(workspace.Id, body.WorkspaceId);
        Assert.Equal(workspace.Name, body.WorkspaceName);
    }

    [Fact]
    public async Task Login_WithWrongPassword_ReturnsUnauthorized()
    {
        var workspace = TestData.Workspace();
        var user = TestData.User(password: "correct-password");
        var member = TestData.Member(workspace, user, "owner");
        await WithDb(async db =>
        {
            db.Workspaces.Add(workspace);
            db.Users.Add(user);
            db.WorkspaceMembers.Add(member);
            await db.SaveChangesAsync();
        });

        var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email, "wrong-password"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithUnknownEmail_ReturnsUnauthorized()
    {
        var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest($"{Guid.NewGuid():N}@nope.local", "whatever"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GoogleExchange_NewVerifiedUser_ProvisionsUserAndWorkspace()
    {
        var email = $"{Guid.NewGuid():N}@gmail.example";
        GoogleOAuth.NextUserInfo = new GoogleUserInfo("google-sub-1", email, true, "New Googler");

        var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/google/exchange", new GoogleExchangeRequest("fake-code"));

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body!.Token));
        Assert.Contains("New Googler", body.WorkspaceName);

        var user = await WithDb(db => db.Users.SingleAsync(u => u.Email == email));
        Assert.Equal("New Googler", user.Name);
        var membership = await WithDb(db => db.WorkspaceMembers.SingleAsync(m => m.UserId == user.Id));
        Assert.Equal("owner", membership.Role);
        Assert.Equal(body.WorkspaceId, membership.WorkspaceId);
    }

    [Fact]
    public async Task GoogleExchange_ExistingUser_LogsIntoExistingWorkspace()
    {
        var workspace = TestData.Workspace();
        var user = TestData.User();
        var member = TestData.Member(workspace, user, "admin");
        await WithDb(async db =>
        {
            db.Workspaces.Add(workspace);
            db.Users.Add(user);
            db.WorkspaceMembers.Add(member);
            await db.SaveChangesAsync();
        });
        GoogleOAuth.NextUserInfo = new GoogleUserInfo("google-sub-2", user.Email, true, "Existing User");

        var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/google/exchange", new GoogleExchangeRequest("fake-code"));

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.Equal(workspace.Id, body!.WorkspaceId);

        // No duplicate user/workspace created for a repeat sign-in.
        var userCount = await WithDb(db => db.Users.CountAsync(u => u.Email == user.Email));
        Assert.Equal(1, userCount);
    }

    [Fact]
    public async Task GoogleExchange_UnverifiedEmail_ReturnsUnauthorized()
    {
        GoogleOAuth.NextUserInfo = new GoogleUserInfo("google-sub-3", $"{Guid.NewGuid():N}@gmail.example", false, "Unverified");

        var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/google/exchange", new GoogleExchangeRequest("fake-code"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GoogleExchange_WhenGoogleCallFails_ReturnsUnauthorized()
    {
        GoogleOAuth.NextException = new InvalidOperationException("GoogleOAuth:ClientId/ClientSecret/RedirectUri are not configured");

        var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/google/exchange", new GoogleExchangeRequest("fake-code"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
