using System.Net;
using System.Net.Http.Json;
using CrmApi.Dtos;

namespace CrmApi.Tests;

[Collection(CrmApiCollection.Name)]
public class MeControllerTests(CrmApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Get_ReturnsDefaultTimezoneAndNoName()
    {
        var ws = await SeedWorkspaceAsync();

        var me = await ws.Client.GetFromJsonAsync<MeDto>("/api/me");

        Assert.NotNull(me);
        Assert.Equal(ws.User.Id, me!.UserId);
        Assert.Equal(ws.User.Email, me.Email);
        Assert.Equal("owner", me.Role);
        Assert.Equal("UTC", me.Timezone);
        Assert.Null(me.Name);
    }

    [Fact]
    public async Task Update_PersistsNameAndTimezone()
    {
        var ws = await SeedWorkspaceAsync();

        var response = await ws.Client.PutAsJsonAsync("/api/me", new UpdateMeRequest("Ada Lovelace", "America/New_York"));

        response.EnsureSuccessStatusCode();
        var dto = await response.Content.ReadFromJsonAsync<MeDto>();
        Assert.Equal("Ada Lovelace", dto!.Name);
        Assert.Equal("America/New_York", dto.Timezone);

        var refetched = await ws.Client.GetFromJsonAsync<MeDto>("/api/me");
        Assert.Equal("Ada Lovelace", refetched!.Name);
        Assert.Equal("America/New_York", refetched.Timezone);
    }

    [Fact]
    public async Task Update_WithUnknownTimezone_ReturnsBadRequest()
    {
        var ws = await SeedWorkspaceAsync();

        var response = await ws.Client.PutAsJsonAsync("/api/me", new UpdateMeRequest("Ada", "Not/A_Zone"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_OnlyAffectsCallingUsersOwnMembership()
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
        var secondClient = AuthedClient(secondUser.Id, ws.Workspace.Id, "member");

        var response = await secondClient.PutAsJsonAsync("/api/me", new UpdateMeRequest("Second User", "Europe/London"));
        response.EnsureSuccessStatusCode();

        var ownerMe = await ws.Client.GetFromJsonAsync<MeDto>("/api/me");
        Assert.Null(ownerMe!.Name);
        Assert.Equal("UTC", ownerMe.Timezone);
    }
}
