using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CrmApi.Dtos;

namespace CrmApi.Tests;

[Collection(CrmApiCollection.Name)]
public class WorkspaceSettingsControllerTests(CrmApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Get_WithNoSettingsRow_ReturnsEmptyDefaults()
    {
        var ws = await SeedWorkspaceAsync();

        var settings = await ws.Client.GetFromJsonAsync<WorkspaceSettingsDto>("/api/workspace/settings");

        Assert.NotNull(settings);
        Assert.Empty(settings!.Terminology);
        Assert.Empty(settings.EnabledModules);
        Assert.Equal("USD", settings.DefaultCurrency);
    }

    [Fact]
    public async Task Update_AsOwner_PersistsDefaultCurrency()
    {
        var ws = await SeedWorkspaceAsync();

        var response = await ws.Client.PutAsJsonAsync("/api/workspace/settings",
            new UpdateWorkspaceSettingsRequest(null, null, "EUR"));

        response.EnsureSuccessStatusCode();
        var dto = await response.Content.ReadFromJsonAsync<WorkspaceSettingsDto>();
        Assert.Equal("EUR", dto!.DefaultCurrency);

        var refetched = await ws.Client.GetFromJsonAsync<WorkspaceSettingsDto>("/api/workspace/settings");
        Assert.Equal("EUR", refetched!.DefaultCurrency);
    }

    [Theory]
    [InlineData("eur")]
    [InlineData("EU")]
    [InlineData("DOLLARS")]
    public async Task Update_WithInvalidCurrencyCode_ReturnsBadRequest(string currency)
    {
        var ws = await SeedWorkspaceAsync();

        var response = await ws.Client.PutAsJsonAsync("/api/workspace/settings",
            new UpdateWorkspaceSettingsRequest(null, null, currency));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_AsOwner_PersistsTerminology()
    {
        var ws = await SeedWorkspaceAsync();
        var terminology = new Dictionary<string, JsonElement>
        {
            ["deal"] = JsonSerializer.SerializeToElement(new { singular = "Listing", plural = "Listings" }),
        };

        var response = await ws.Client.PutAsJsonAsync("/api/workspace/settings",
            new UpdateWorkspaceSettingsRequest(terminology, ["listings"]));

        response.EnsureSuccessStatusCode();
        var dto = await response.Content.ReadFromJsonAsync<WorkspaceSettingsDto>();
        Assert.NotNull(dto);
        Assert.Contains("deal", dto!.Terminology.Keys);
        Assert.Contains("listings", dto.EnabledModules);

        // Round-trips on a fresh GET too, not just the PUT response.
        var refetched = await ws.Client.GetFromJsonAsync<WorkspaceSettingsDto>("/api/workspace/settings");
        Assert.Contains("deal", refetched!.Terminology.Keys);
    }

    [Fact]
    public async Task Update_AsMember_ReturnsForbidden()
    {
        var ws = await SeedWorkspaceAsync();
        var memberClient = AuthedClient(ws.User.Id, ws.Workspace.Id, "member");

        var response = await memberClient.PutAsJsonAsync("/api/workspace/settings",
            new UpdateWorkspaceSettingsRequest(new Dictionary<string, JsonElement>(), []));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Update_AsAdmin_Succeeds()
    {
        var ws = await SeedWorkspaceAsync();
        var adminClient = AuthedClient(ws.User.Id, ws.Workspace.Id, "admin");

        var response = await adminClient.PutAsJsonAsync("/api/workspace/settings",
            new UpdateWorkspaceSettingsRequest(new Dictionary<string, JsonElement>(), ["listings"]));

        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Settings_AreIsolatedPerWorkspace()
    {
        var ws1 = await SeedWorkspaceAsync();
        var ws2 = await SeedWorkspaceAsync();

        var terminology = new Dictionary<string, JsonElement>
        {
            ["deal"] = JsonSerializer.SerializeToElement(new { singular = "Listing" }),
        };
        var update = await ws1.Client.PutAsJsonAsync("/api/workspace/settings", new UpdateWorkspaceSettingsRequest(terminology, null));
        update.EnsureSuccessStatusCode();

        var ws2Settings = await ws2.Client.GetFromJsonAsync<WorkspaceSettingsDto>("/api/workspace/settings");
        Assert.Empty(ws2Settings!.Terminology);
    }
}
