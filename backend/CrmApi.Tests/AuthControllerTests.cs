using System.Net;
using System.Net.Http.Json;
using CrmApi.Dtos;

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
}
