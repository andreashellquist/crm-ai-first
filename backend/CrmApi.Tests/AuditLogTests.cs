using System.Net;
using System.Net.Http.Json;
using CrmApi.Dtos;
using Microsoft.EntityFrameworkCore;

namespace CrmApi.Tests;

[Collection(CrmApiCollection.Name)]
public class AuditLogTests(CrmApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Get_AsMember_ReturnsForbidden()
    {
        var ws = await SeedWorkspaceAsync();
        var memberClient = AuthedClient(ws.User.Id, ws.Workspace.Id, "member");

        var response = await memberClient.GetAsync("/api/audit-log");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task MemberRoleChange_WritesAuditLogEntry()
    {
        var ws = await SeedWorkspaceAsync();
        var secondUser = TestData.User();
        var secondMember = TestData.Member(ws.Workspace, secondUser, "member");
        await WithDb(async db =>
        {
            db.Users.Add(secondUser);
            db.WorkspaceMembers.Add(secondMember);
            await db.SaveChangesAsync();
        });

        var response = await ws.Client.PutAsJsonAsync($"/api/members/{secondMember.Id}/role", new UpdateMemberRoleRequest("admin"));
        response.EnsureSuccessStatusCode();

        var logs = await ws.Client.GetFromJsonAsync<List<AuditLogDto>>("/api/audit-log");
        var entry = logs!.Single(l => l.Action == "member.role_changed" && l.TargetId == secondMember.Id);
        Assert.Equal(ws.User.Id, entry.ActorUserId);
        Assert.Contains("previousRole", entry.Metadata);
        Assert.Contains("admin", entry.Metadata);
    }

    [Fact]
    public async Task MemberRemove_WritesAuditLogEntry()
    {
        var ws = await SeedWorkspaceAsync();
        var secondUser = TestData.User();
        var secondMember = TestData.Member(ws.Workspace, secondUser, "member");
        await WithDb(async db =>
        {
            db.Users.Add(secondUser);
            db.WorkspaceMembers.Add(secondMember);
            await db.SaveChangesAsync();
        });

        var response = await ws.Client.DeleteAsync($"/api/members/{secondMember.Id}");
        response.EnsureSuccessStatusCode();

        var logs = await ws.Client.GetFromJsonAsync<List<AuditLogDto>>("/api/audit-log");
        Assert.Contains(logs!, l => l.Action == "member.removed" && l.TargetId == secondMember.Id);
    }

    [Fact]
    public async Task RoleCreateUpdateDelete_EachWritesAnAuditLogEntry()
    {
        var ws = await SeedWorkspaceAsync();
        var roleName = $"Role {Guid.NewGuid():N}";

        var createResponse = await ws.Client.PostAsJsonAsync("/api/roles", new CreateRoleRequest(roleName, ["webhooks:manage"]));
        createResponse.EnsureSuccessStatusCode();
        var role = await createResponse.Content.ReadFromJsonAsync<RoleDto>();

        var updateResponse = await ws.Client.PutAsJsonAsync($"/api/roles/{role!.Id}", new UpdateRoleRequest(["api_keys:manage"]));
        updateResponse.EnsureSuccessStatusCode();

        var deleteResponse = await ws.Client.DeleteAsync($"/api/roles/{role.Id}");
        deleteResponse.EnsureSuccessStatusCode();

        var logs = await ws.Client.GetFromJsonAsync<List<AuditLogDto>>("/api/audit-log");
        Assert.Contains(logs!, l => l.Action == "role.created" && l.TargetId == role.Id);
        Assert.Contains(logs!, l => l.Action == "role.updated" && l.TargetId == role.Id);
        Assert.Contains(logs!, l => l.Action == "role.deleted" && l.TargetId == role.Id);
    }

    [Fact]
    public async Task ApiKeyCreateAndRevoke_EachWritesAnAuditLogEntry()
    {
        var ws = await SeedWorkspaceAsync();

        var createResponse = await ws.Client.PostAsJsonAsync("/api/api-keys", new CreateApiKeyRequest("Test Key", ["contacts:read"]));
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<CreateApiKeyResponse>();

        var revokeResponse = await ws.Client.DeleteAsync($"/api/api-keys/{created!.Key.Id}");
        revokeResponse.EnsureSuccessStatusCode();

        var logs = await ws.Client.GetFromJsonAsync<List<AuditLogDto>>("/api/audit-log");
        Assert.Contains(logs!, l => l.Action == "api_key.created" && l.TargetId == created.Key.Id);
        Assert.Contains(logs!, l => l.Action == "api_key.revoked" && l.TargetId == created.Key.Id);
    }

    [Fact]
    public async Task SsoConnectionUpdateAndDelete_EachWritesAnAuditLogEntry()
    {
        var ws = await SeedWorkspaceAsync();
        var domain = $"{Guid.NewGuid():N}.example";

        var updateResponse = await ws.Client.PutAsJsonAsync("/api/workspace/sso",
            new UpdateSsoConnectionRequest("https://idp.example.com", "client-1", "secret-1", domain, false, true));
        updateResponse.EnsureSuccessStatusCode();
        var connection = await updateResponse.Content.ReadFromJsonAsync<SsoConnectionDto>();

        var deleteResponse = await ws.Client.DeleteAsync("/api/workspace/sso");
        deleteResponse.EnsureSuccessStatusCode();

        var logs = await ws.Client.GetFromJsonAsync<List<AuditLogDto>>("/api/audit-log");
        Assert.Contains(logs!, l => l.Action == "sso_connection.updated" && l.TargetId == connection!.Id);
        Assert.Contains(logs!, l => l.Action == "sso_connection.removed" && l.TargetId == connection!.Id);
    }

    [Fact]
    public async Task WorkspaceSettingsUpdate_WritesAuditLogEntry()
    {
        var ws = await SeedWorkspaceAsync();

        var response = await ws.Client.PutAsJsonAsync("/api/workspace/settings",
            new UpdateWorkspaceSettingsRequest(null, ["listings"]));
        response.EnsureSuccessStatusCode();

        var logs = await ws.Client.GetFromJsonAsync<List<AuditLogDto>>("/api/audit-log");
        Assert.Contains(logs!, l => l.Action == "workspace_settings.updated" && l.TargetId == ws.Workspace.Id);
    }

    [Fact]
    public async Task AuditLog_IsIsolatedPerWorkspace()
    {
        var ws1 = await SeedWorkspaceAsync();
        var ws2 = await SeedWorkspaceAsync();

        var response = await ws1.Client.PutAsJsonAsync("/api/workspace/settings", new UpdateWorkspaceSettingsRequest(null, ["listings"]));
        response.EnsureSuccessStatusCode();

        var ws2Logs = await ws2.Client.GetFromJsonAsync<List<AuditLogDto>>("/api/audit-log");
        Assert.DoesNotContain(ws2Logs!, l => l.TargetId == ws1.Workspace.Id);
    }

    [Fact]
    public async Task RemovingActorUser_KeepsTheAuditLogEntryWithNullActor()
    {
        var ws = await SeedWorkspaceAsync();
        var secondUser = TestData.User();
        var secondMember = TestData.Member(ws.Workspace, secondUser, "admin");
        await WithDb(async db =>
        {
            db.Users.Add(secondUser);
            db.WorkspaceMembers.Add(secondMember);
            await db.SaveChangesAsync();
        });
        var secondClient = AuthedClient(secondUser.Id, ws.Workspace.Id, "admin");

        var response = await secondClient.PutAsJsonAsync("/api/workspace/settings", new UpdateWorkspaceSettingsRequest(null, ["listings"]));
        response.EnsureSuccessStatusCode();

        // Deleting the actor's User row must not cascade-delete the audit
        // trail entry they created (AppDbContext configures ActorUser with
        // DeleteBehavior.SetNull specifically to guarantee this).
        await WithDb(async db =>
        {
            var user = await db.Users.FindAsync(secondUser.Id);
            db.Users.Remove(user!);
            await db.SaveChangesAsync();
        });

        var logs = await ws.Client.GetFromJsonAsync<List<AuditLogDto>>("/api/audit-log");
        var entry = logs!.Single(l => l.Action == "workspace_settings.updated" && l.TargetId == ws.Workspace.Id);
        Assert.Null(entry.ActorUserId);
    }
}
