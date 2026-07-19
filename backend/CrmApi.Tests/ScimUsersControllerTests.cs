using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CrmApi.Dtos;
using Microsoft.EntityFrameworkCore;

namespace CrmApi.Tests;

[Collection(CrmApiCollection.Name)]
public class ScimUsersControllerTests(CrmApiFactory factory) : IntegrationTestBase(factory)
{
    private async Task<HttpClient> ScimClientAsync(WorkspaceScenario ws)
    {
        var rawKey = await CreateApiKeyAsync(ws.Workspace.Id, ws.User.Id, "scim:users");
        return ApiKeyClient(rawKey);
    }

    [Fact]
    public async Task Create_NewUserName_ProvisionsUserAndActiveMembership()
    {
        var ws = await SeedWorkspaceAsync();
        var scim = await ScimClientAsync(ws);
        var userName = $"{Guid.NewGuid():N}@idp.example";

        var response = await scim.PostAsJsonAsync("/api/scim/v2/Users",
            new ScimCreateUserRequest(userName, new ScimName("Jordan Provisioned", null), null));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ScimUserDto>();
        Assert.NotNull(body);
        Assert.Equal(userName, body!.UserName);
        Assert.True(body.Active);
        Assert.Equal("Jordan Provisioned", body.Name.GivenName);
        Assert.Contains("urn:ietf:params:scim:schemas:core:2.0:User", body.Schemas);

        var member = await WithDb(db => db.WorkspaceMembers.SingleAsync(m => m.Id == body.Id));
        Assert.Equal("member", member.Role);
        Assert.True(member.IsActive);
    }

    [Fact]
    public async Task Create_DuplicateUserNameInSameWorkspace_ReturnsConflict()
    {
        var ws = await SeedWorkspaceAsync();
        var scim = await ScimClientAsync(ws);
        var userName = $"{Guid.NewGuid():N}@idp.example";
        await scim.PostAsJsonAsync("/api/scim/v2/Users", new ScimCreateUserRequest(userName, null, null));

        var second = await scim.PostAsJsonAsync("/api/scim/v2/Users", new ScimCreateUserRequest(userName, null, null));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var error = await second.Content.ReadFromJsonAsync<ScimError>();
        Assert.Equal("uniqueness", error!.ScimType);
    }

    [Fact]
    public async Task List_FiltersByUserNameEq()
    {
        var ws = await SeedWorkspaceAsync();
        var scim = await ScimClientAsync(ws);
        var userName = $"{Guid.NewGuid():N}@idp.example";
        await scim.PostAsJsonAsync("/api/scim/v2/Users", new ScimCreateUserRequest(userName, null, null));
        await scim.PostAsJsonAsync("/api/scim/v2/Users", new ScimCreateUserRequest($"{Guid.NewGuid():N}@idp.example", null, null));

        var response = await scim.GetAsync($"/api/scim/v2/Users?filter=userName eq \"{userName}\"");
        response.EnsureSuccessStatusCode();
        var list = await response.Content.ReadFromJsonAsync<ScimListResponse>();

        Assert.Equal(1, list!.TotalResults);
        Assert.Equal(userName, list.Resources[0].UserName);
    }

    [Fact]
    public async Task List_UnsupportedFilter_ReturnsBadRequestWithScimErrorShape()
    {
        var ws = await SeedWorkspaceAsync();
        var scim = await ScimClientAsync(ws);

        var response = await scim.GetAsync("/api/scim/v2/Users?filter=displayName co \"x\"");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ScimError>();
        Assert.Equal("invalidFilter", error!.ScimType);
        Assert.Contains("urn:ietf:params:scim:api:messages:2.0:Error", error.Schemas);
    }

    [Fact]
    public async Task Get_UnknownId_ReturnsScimShapedNotFound()
    {
        var ws = await SeedWorkspaceAsync();
        var scim = await ScimClientAsync(ws);

        var response = await scim.GetAsync("/api/scim/v2/Users/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ScimError>();
        Assert.Equal("404", error!.Status);
    }

    [Fact]
    public async Task Patch_ReplaceActiveFalse_DeactivatesAndBlocksLogin()
    {
        var ws = await SeedWorkspaceAsync();
        var scim = await ScimClientAsync(ws);
        var password = "provisioned-password-123";
        var user = TestData.User(password: password);
        var member = TestData.Member(ws.Workspace, user, "member");
        await WithDb(async db =>
        {
            db.Users.Add(user);
            db.WorkspaceMembers.Add(member);
            await db.SaveChangesAsync();
        });

        // Sanity: login works before deactivation.
        var loginResponse = await Factory.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email, password));
        loginResponse.EnsureSuccessStatusCode();

        var patchBody = new ScimPatchRequest(
            ["urn:ietf:params:scim:api:messages:2.0:PatchOp"],
            [new ScimPatchOperation("replace", "active", JsonSerializer.SerializeToElement(false))]);
        var patchResponse = await scim.PatchAsJsonAsync($"/api/scim/v2/Users/{member.Id}", patchBody);
        patchResponse.EnsureSuccessStatusCode();
        var patched = await patchResponse.Content.ReadFromJsonAsync<ScimUserDto>();
        Assert.False(patched!.Active);

        var blockedLogin = await Factory.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email, password));
        Assert.Equal(HttpStatusCode.Unauthorized, blockedLogin.StatusCode);
    }

    [Fact]
    public async Task Patch_UnsupportedOperation_ReturnsBadRequest()
    {
        var ws = await SeedWorkspaceAsync();
        var scim = await ScimClientAsync(ws);
        var create = await scim.PostAsJsonAsync("/api/scim/v2/Users", new ScimCreateUserRequest($"{Guid.NewGuid():N}@idp.example", null, null));
        var created = await create.Content.ReadFromJsonAsync<ScimUserDto>();

        var patchBody = new ScimPatchRequest(null,
            [new ScimPatchOperation("replace", "userName", JsonSerializer.SerializeToElement("someone-else@idp.example"))]);
        var response = await scim.PatchAsJsonAsync($"/api/scim/v2/Users/{created!.Id}", patchBody);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Delete_RemovesMembership()
    {
        var ws = await SeedWorkspaceAsync();
        var scim = await ScimClientAsync(ws);
        var create = await scim.PostAsJsonAsync("/api/scim/v2/Users", new ScimCreateUserRequest($"{Guid.NewGuid():N}@idp.example", null, null));
        var created = await create.Content.ReadFromJsonAsync<ScimUserDto>();

        var delete = await scim.DeleteAsync($"/api/scim/v2/Users/{created!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var get = await scim.GetAsync($"/api/scim/v2/Users/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    [Fact]
    public async Task List_WithoutScimScope_ReturnsForbidden()
    {
        var ws = await SeedWorkspaceAsync();
        var rawKey = await CreateApiKeyAsync(ws.Workspace.Id, ws.User.Id, "contacts:read"); // no scim:users
        var client = ApiKeyClient(rawKey);

        var response = await client.GetAsync("/api/scim/v2/Users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task List_NeverReturnsAnotherWorkspacesMembers()
    {
        var owner = await SeedWorkspaceAsync();
        var intruder = await SeedWorkspaceAsync();
        var ownerScim = await ScimClientAsync(owner);
        var intruderScim = await ScimClientAsync(intruder);
        await ownerScim.PostAsJsonAsync("/api/scim/v2/Users", new ScimCreateUserRequest($"{Guid.NewGuid():N}@idp.example", null, null));

        var intruderList = await intruderScim.GetFromJsonAsync<ScimListResponse>("/api/scim/v2/Users");

        // Only the intruder workspace's own seeded owner member, never the
        // other workspace's provisioned user.
        Assert.Equal(1, intruderList!.TotalResults);
    }
}
