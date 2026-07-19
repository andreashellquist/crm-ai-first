using System.Net;
using System.Net.Http.Json;
using CrmApi.Dtos;

namespace CrmApi.Tests;

[Collection(CrmApiCollection.Name)]
public class RolesControllerTests(CrmApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task List_IncludesTheThreeSystemRolesWithFixedPermissions()
    {
        var ws = await SeedWorkspaceAsync();

        var roles = await ws.Client.GetFromJsonAsync<List<RoleDto>>("/api/roles");

        Assert.NotNull(roles);
        var owner = roles!.Single(r => r.Name == "owner");
        Assert.True(owner.IsSystem);
        Assert.Null(owner.Id);
        Assert.Contains("roles:manage", owner.Permissions);

        var admin = roles.Single(r => r.Name == "admin");
        Assert.True(admin.IsSystem);
        Assert.DoesNotContain("roles:manage", admin.Permissions); // only owner grants/manages roles

        var member = roles.Single(r => r.Name == "member");
        Assert.Empty(member.Permissions);
    }

    [Fact]
    public async Task Create_AsOwner_PersistsCustomRole()
    {
        var ws = await SeedWorkspaceAsync();

        var response = await ws.Client.PostAsJsonAsync("/api/roles",
            new CreateRoleRequest("Field Editor", ["fields:manage"]));

        response.EnsureSuccessStatusCode();
        var role = await response.Content.ReadFromJsonAsync<RoleDto>();
        Assert.NotNull(role);
        Assert.False(role!.IsSystem);
        Assert.Contains("fields:manage", role.Permissions);

        var list = await ws.Client.GetFromJsonAsync<List<RoleDto>>("/api/roles");
        Assert.Contains(list!, r => r.Name == "Field Editor");
    }

    [Fact]
    public async Task Create_AsAdmin_ReturnsForbidden()
    {
        var ws = await SeedWorkspaceAsync();
        var adminClient = AuthedClient(ws.User.Id, ws.Workspace.Id, "admin");

        var response = await adminClient.PostAsJsonAsync("/api/roles", new CreateRoleRequest("Sneaky", ["roles:manage"]));

        // Admin's system permission set doesn't include roles:manage — only
        // owner can create/edit roles, so an admin can never grant itself
        // (or a custom role) more access than an owner already allowed.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_NameCollidesWithSystemRole_ReturnsBadRequest()
    {
        var ws = await SeedWorkspaceAsync();

        var response = await ws.Client.PostAsJsonAsync("/api/roles", new CreateRoleRequest("admin", ["fields:manage"]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_UnknownPermission_ReturnsBadRequest()
    {
        var ws = await SeedWorkspaceAsync();

        var response = await ws.Client.PostAsJsonAsync("/api/roles", new CreateRoleRequest("Odd Role", ["not:a-real-permission"]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_DuplicateName_ReturnsConflict()
    {
        var ws = await SeedWorkspaceAsync();
        await ws.Client.PostAsJsonAsync("/api/roles", new CreateRoleRequest("Field Editor", ["fields:manage"]));

        var response = await ws.Client.PostAsJsonAsync("/api/roles", new CreateRoleRequest("Field Editor", ["webhooks:manage"]));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Update_ChangesPermissions()
    {
        var ws = await SeedWorkspaceAsync();
        var create = await ws.Client.PostAsJsonAsync("/api/roles", new CreateRoleRequest("Field Editor", ["fields:manage"]));
        var role = await create.Content.ReadFromJsonAsync<RoleDto>();

        var update = await ws.Client.PutAsJsonAsync($"/api/roles/{role!.Id}", new UpdateRoleRequest(["fields:manage", "webhooks:manage"]));

        update.EnsureSuccessStatusCode();
        var updated = await update.Content.ReadFromJsonAsync<RoleDto>();
        Assert.Contains("webhooks:manage", updated!.Permissions);
    }

    [Fact]
    public async Task Delete_RoleInUse_ReturnsConflict()
    {
        var ws = await SeedWorkspaceAsync();
        var create = await ws.Client.PostAsJsonAsync("/api/roles", new CreateRoleRequest("Field Editor", ["fields:manage"]));
        var role = await create.Content.ReadFromJsonAsync<RoleDto>();
        var otherUser = TestData.User();
        var otherMember = TestData.Member(ws.Workspace, otherUser, "Field Editor");
        await WithDb(async db =>
        {
            db.Users.Add(otherUser);
            db.WorkspaceMembers.Add(otherMember);
            await db.SaveChangesAsync();
        });

        var response = await ws.Client.DeleteAsync($"/api/roles/{role!.Id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Delete_UnusedRole_Succeeds()
    {
        var ws = await SeedWorkspaceAsync();
        var create = await ws.Client.PostAsJsonAsync("/api/roles", new CreateRoleRequest("Unused Role", ["fields:manage"]));
        var role = await create.Content.ReadFromJsonAsync<RoleDto>();

        var response = await ws.Client.DeleteAsync($"/api/roles/{role!.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task List_NeverReturnsAnotherWorkspacesCustomRoles()
    {
        var owner = await SeedWorkspaceAsync();
        var intruder = await SeedWorkspaceAsync();
        await owner.Client.PostAsJsonAsync("/api/roles", new CreateRoleRequest("Owner Only Role", ["fields:manage"]));

        var intruderRoles = await intruder.Client.GetFromJsonAsync<List<RoleDto>>("/api/roles");

        Assert.DoesNotContain(intruderRoles!, r => r.Name == "Owner Only Role");
    }
}
