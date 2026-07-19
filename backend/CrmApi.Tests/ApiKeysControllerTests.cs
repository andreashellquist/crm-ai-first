using System.Net;
using System.Net.Http.Json;
using CrmApi.Dtos;

namespace CrmApi.Tests;

[Collection(CrmApiCollection.Name)]
public class ApiKeysControllerTests(CrmApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Create_AsOwner_ReturnsRawKeyOnce()
    {
        var ws = await SeedWorkspaceAsync();

        var response = await ws.Client.PostAsJsonAsync("/api/api-keys",
            new CreateApiKeyRequest("Zapier integration", ["contacts:read", "deals:read"]));

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<CreateApiKeyResponse>();
        Assert.NotNull(body);
        Assert.StartsWith("crm_live_", body!.RawKey);
        Assert.Equal("Zapier integration", body.Key.Name);
        Assert.Contains("contacts:read", body.Key.Scopes);

        // The raw key is never persisted — only its hash — so re-fetching
        // the key list never includes it again.
        var list = await ws.Client.GetFromJsonAsync<List<ApiKeyDto>>("/api/api-keys");
        Assert.Single(list!);
        Assert.Equal(body.Key.Id, list![0].Id);
    }

    [Fact]
    public async Task Create_UnknownScope_ReturnsBadRequest()
    {
        var ws = await SeedWorkspaceAsync();

        var response = await ws.Client.PostAsJsonAsync("/api/api-keys",
            new CreateApiKeyRequest("Bad key", ["not:a-real-scope"]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_NoScopes_ReturnsBadRequest()
    {
        var ws = await SeedWorkspaceAsync();

        var response = await ws.Client.PostAsJsonAsync("/api/api-keys", new CreateApiKeyRequest("Empty", []));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_AsMember_ReturnsForbidden()
    {
        var ws = await SeedWorkspaceAsync();
        var memberClient = AuthedClient(ws.User.Id, ws.Workspace.Id, "member");

        var response = await memberClient.PostAsJsonAsync("/api/api-keys",
            new CreateApiKeyRequest("Member key", ["contacts:read"]));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Revoke_ThenKeyNoLongerAuthenticates()
    {
        var ws = await SeedWorkspaceAsync();
        var create = await ws.Client.PostAsJsonAsync("/api/api-keys",
            new CreateApiKeyRequest("Revoke me", ["contacts:read"]));
        var body = await create.Content.ReadFromJsonAsync<CreateApiKeyResponse>();

        var v1ClientBefore = ApiKeyClient(body!.RawKey);
        var beforeResponse = await v1ClientBefore.GetAsync("/api/v1/contacts");
        Assert.Equal(HttpStatusCode.OK, beforeResponse.StatusCode);

        var revoke = await ws.Client.DeleteAsync($"/api/api-keys/{body.Key.Id}");
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);

        var v1ClientAfter = ApiKeyClient(body.RawKey);
        var afterResponse = await v1ClientAfter.GetAsync("/api/v1/contacts");
        Assert.Equal(HttpStatusCode.Unauthorized, afterResponse.StatusCode);
    }

    [Fact]
    public async Task List_OnlyReturnsCallersWorkspaceKeys()
    {
        var owner = await SeedWorkspaceAsync();
        var intruder = await SeedWorkspaceAsync();
        await owner.Client.PostAsJsonAsync("/api/api-keys", new CreateApiKeyRequest("Owner's key", ["contacts:read"]));

        var intruderKeys = await intruder.Client.GetFromJsonAsync<List<ApiKeyDto>>("/api/api-keys");

        Assert.Empty(intruderKeys!);
    }
}
