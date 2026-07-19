using System.Net;
using System.Net.Http.Json;
using CrmApi.Dtos;
using Microsoft.EntityFrameworkCore;

namespace CrmApi.Tests;

[Collection(CrmApiCollection.Name)]
public class MembersControllerTests(CrmApiFactory factory) : IntegrationTestBase(factory)
{
    private async Task<(CrmApi.Models.User User, CrmApi.Models.WorkspaceMember Member)> AddMemberAsync(
        WorkspaceScenario ws, string role = "member")
    {
        var user = TestData.User();
        var member = TestData.Member(ws.Workspace, user, role);
        await WithDb(async db =>
        {
            db.Users.Add(user);
            db.WorkspaceMembers.Add(member);
            await db.SaveChangesAsync();
        });
        return (user, member);
    }

    [Fact]
    public async Task List_ReturnsWorkspaceMembers()
    {
        var ws = await SeedWorkspaceAsync();
        await AddMemberAsync(ws);

        var members = await ws.Client.GetFromJsonAsync<List<MemberDto>>("/api/members");

        Assert.Equal(2, members!.Count);
    }

    [Fact]
    public async Task UpdateRole_AsOwner_ChangesMemberRole()
    {
        var ws = await SeedWorkspaceAsync();
        var (_, member) = await AddMemberAsync(ws);

        var response = await ws.Client.PutAsJsonAsync($"/api/members/{member.Id}/role", new UpdateMemberRoleRequest("admin"));

        response.EnsureSuccessStatusCode();
        var updated = await response.Content.ReadFromJsonAsync<MemberDto>();
        Assert.Equal("admin", updated!.Role);
    }

    [Fact]
    public async Task UpdateRole_ToCustomRoleName_Succeeds()
    {
        var ws = await SeedWorkspaceAsync();
        var (_, member) = await AddMemberAsync(ws);
        await ws.Client.PostAsJsonAsync("/api/roles", new CreateRoleRequest("Field Editor", ["fields:manage"]));

        var response = await ws.Client.PutAsJsonAsync($"/api/members/{member.Id}/role", new UpdateMemberRoleRequest("Field Editor"));

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task UpdateRole_ToUnknownRoleName_ReturnsBadRequest()
    {
        var ws = await SeedWorkspaceAsync();
        var (_, member) = await AddMemberAsync(ws);

        var response = await ws.Client.PutAsJsonAsync($"/api/members/{member.Id}/role", new UpdateMemberRoleRequest("not-a-role"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateRole_AsMember_ReturnsForbidden()
    {
        var ws = await SeedWorkspaceAsync();
        var (_, target) = await AddMemberAsync(ws);
        var memberClient = AuthedClient(ws.User.Id, ws.Workspace.Id, "member");

        var response = await memberClient.PutAsJsonAsync($"/api/members/{target.Id}/role", new UpdateMemberRoleRequest("admin"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateRole_DemotingTheOnlyOwner_ReturnsConflict()
    {
        var ws = await SeedWorkspaceAsync(); // owner is the only owner in this workspace
        var ownerMemberId = await WithDb(db => db.WorkspaceMembers
            .Where(m => m.WorkspaceId == ws.Workspace.Id && m.UserId == ws.User.Id)
            .Select(m => m.Id).SingleAsync());

        var response = await ws.Client.PutAsJsonAsync($"/api/members/{ownerMemberId}/role", new UpdateMemberRoleRequest("admin"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task UpdateRole_DemotingOneOfSeveralOwners_Succeeds()
    {
        var ws = await SeedWorkspaceAsync();
        var (_, secondOwner) = await AddMemberAsync(ws, "owner");
        var firstOwnerMemberId = await WithDb(db => db.WorkspaceMembers
            .Where(m => m.WorkspaceId == ws.Workspace.Id && m.UserId == ws.User.Id)
            .Select(m => m.Id).SingleAsync());

        var response = await ws.Client.PutAsJsonAsync($"/api/members/{firstOwnerMemberId}/role", new UpdateMemberRoleRequest("admin"));

        response.EnsureSuccessStatusCode();
        Assert.NotEqual("", secondOwner.Id); // sanity — second owner exists so demotion above was safe
    }

    [Fact]
    public async Task Remove_TheOnlyOwner_ReturnsConflict()
    {
        var ws = await SeedWorkspaceAsync();
        var ownerMemberId = await WithDb(db => db.WorkspaceMembers
            .Where(m => m.WorkspaceId == ws.Workspace.Id && m.UserId == ws.User.Id)
            .Select(m => m.Id).SingleAsync());

        var response = await ws.Client.DeleteAsync($"/api/members/{ownerMemberId}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Remove_NonOwnerMember_Succeeds()
    {
        var ws = await SeedWorkspaceAsync();
        var (_, member) = await AddMemberAsync(ws);

        var response = await ws.Client.DeleteAsync($"/api/members/{member.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var stillThere = await WithDb(db => db.WorkspaceMembers.AnyAsync(m => m.Id == member.Id));
        Assert.False(stillThere);
    }

    [Fact]
    public async Task CustomRolePermission_GrantsExactlyTheScopedAccess_EndToEnd()
    {
        var ws = await SeedWorkspaceAsync();
        var createRole = await ws.Client.PostAsJsonAsync("/api/roles", new CreateRoleRequest("Field Editor", ["fields:manage"]));
        createRole.EnsureSuccessStatusCode();
        var (_, member) = await AddMemberAsync(ws, "Field Editor");
        var fieldEditorClient = AuthedClient(member.UserId, ws.Workspace.Id, "Field Editor");

        // Granted: fields:manage.
        var createField = await fieldEditorClient.PostAsJsonAsync("/api/field-definitions",
            new CreateFieldDefinitionRequest("deal", "custom_key", "Custom Key", "text", null, false, 0));
        Assert.Equal(HttpStatusCode.OK, createField.StatusCode);

        // Not granted: settings:manage.
        var updateSettings = await fieldEditorClient.PutAsJsonAsync("/api/workspace/settings",
            new UpdateWorkspaceSettingsRequest(null, ["listings"]));
        Assert.Equal(HttpStatusCode.Forbidden, updateSettings.StatusCode);
    }

    [Fact]
    public async Task List_NeverReturnsAnotherWorkspacesMembers()
    {
        var owner = await SeedWorkspaceAsync();
        var intruder = await SeedWorkspaceAsync();
        await AddMemberAsync(owner);

        var intruderMembers = await intruder.Client.GetFromJsonAsync<List<MemberDto>>("/api/members");

        Assert.Single(intruderMembers!); // only the intruder workspace's own seeded owner
    }
}
