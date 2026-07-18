using System.Net;
using System.Net.Http.Json;
using CrmApi.Dtos;
using Microsoft.EntityFrameworkCore;

namespace CrmApi.Tests;

[Collection(CrmApiCollection.Name)]
public class FieldDefinitionsControllerTests(CrmApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Create_AsOwner_Persists()
    {
        var ws = await SeedWorkspaceAsync();

        var response = await ws.Client.PostAsJsonAsync("/api/field-definitions",
            new CreateFieldDefinitionRequest("contact", "shoe_size", "Shoe Size", "number", null, false, 0));

        response.EnsureSuccessStatusCode();
        var dto = await response.Content.ReadFromJsonAsync<FieldDefinitionDto>();
        Assert.NotNull(dto);
        Assert.Equal("shoe_size", dto!.Key);

        var count = await WithDb(db => db.FieldDefinitions.CountAsync(f => f.WorkspaceId == ws.Workspace.Id && f.Key == "shoe_size"));
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Create_AsMember_ReturnsForbidden()
    {
        var ws = await SeedWorkspaceAsync();
        var memberClient = AuthedClient(ws.User.Id, ws.Workspace.Id, "member");

        var response = await memberClient.PostAsJsonAsync("/api/field-definitions",
            new CreateFieldDefinitionRequest("contact", "shoe_size", "Shoe Size", "number", null, false, 0));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithInvalidEntityType_ReturnsBadRequest()
    {
        var ws = await SeedWorkspaceAsync();

        var response = await ws.Client.PostAsJsonAsync("/api/field-definitions",
            new CreateFieldDefinitionRequest("listing", "sqft", "Square Feet", "number", null, false, 0));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_SelectFieldWithoutOptions_ReturnsBadRequest()
    {
        var ws = await SeedWorkspaceAsync();

        var response = await ws.Client.PostAsJsonAsync("/api/field-definitions",
            new CreateFieldDefinitionRequest("contact", "source", "Source", "select", null, false, 0));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_DuplicateKeyForSameEntityType_ReturnsConflict()
    {
        var ws = await SeedWorkspaceAsync();
        var first = await ws.Client.PostAsJsonAsync("/api/field-definitions",
            new CreateFieldDefinitionRequest("contact", "source", "Source", "text", null, false, 0));
        first.EnsureSuccessStatusCode();

        var second = await ws.Client.PostAsJsonAsync("/api/field-definitions",
            new CreateFieldDefinitionRequest("contact", "source", "Lead Source", "text", null, false, 1));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Create_SameKeyDifferentEntityType_Succeeds()
    {
        var ws = await SeedWorkspaceAsync();
        var contactField = await ws.Client.PostAsJsonAsync("/api/field-definitions",
            new CreateFieldDefinitionRequest("contact", "source", "Source", "text", null, false, 0));
        var dealField = await ws.Client.PostAsJsonAsync("/api/field-definitions",
            new CreateFieldDefinitionRequest("deal", "source", "Source", "text", null, false, 0));

        Assert.True(contactField.IsSuccessStatusCode);
        Assert.True(dealField.IsSuccessStatusCode);
    }

    [Fact]
    public async Task List_FiltersByEntityType()
    {
        var ws = await SeedWorkspaceAsync();
        await ws.Client.PostAsJsonAsync("/api/field-definitions", new CreateFieldDefinitionRequest("contact", "a", "A", "text", null, false, 0));
        await ws.Client.PostAsJsonAsync("/api/field-definitions", new CreateFieldDefinitionRequest("deal", "b", "B", "text", null, false, 0));

        var contactFields = await ws.Client.GetFromJsonAsync<List<FieldDefinitionDto>>("/api/field-definitions?entityType=contact");

        Assert.NotNull(contactFields);
        Assert.All(contactFields!, f => Assert.Equal("contact", f.EntityType));
        Assert.Contains(contactFields, f => f.Key == "a");
    }

    [Fact]
    public async Task Update_AsMember_ReturnsForbidden()
    {
        var ws = await SeedWorkspaceAsync();
        var created = await ws.Client.PostAsJsonAsync("/api/field-definitions",
            new CreateFieldDefinitionRequest("contact", "source", "Source", "text", null, false, 0));
        var dto = await created.Content.ReadFromJsonAsync<FieldDefinitionDto>();

        var memberClient = AuthedClient(ws.User.Id, ws.Workspace.Id, "member");
        var response = await memberClient.PutAsJsonAsync($"/api/field-definitions/{dto!.Id}",
            new UpdateFieldDefinitionRequest("Lead Source", null, false, 0));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Delete_ForAnotherWorkspacesFieldDefinition_ReturnsNotFound()
    {
        var owner = await SeedWorkspaceAsync();
        var intruder = await SeedWorkspaceAsync();
        var created = await owner.Client.PostAsJsonAsync("/api/field-definitions",
            new CreateFieldDefinitionRequest("contact", "source", "Source", "text", null, false, 0));
        var dto = await created.Content.ReadFromJsonAsync<FieldDefinitionDto>();

        var response = await intruder.Client.DeleteAsync($"/api/field-definitions/{dto!.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var stillExists = await WithDb(db => db.FieldDefinitions.AnyAsync(f => f.Id == dto.Id));
        Assert.True(stillExists);
    }

    [Fact]
    public async Task Delete_AsOwner_Removes()
    {
        var ws = await SeedWorkspaceAsync();
        var created = await ws.Client.PostAsJsonAsync("/api/field-definitions",
            new CreateFieldDefinitionRequest("contact", "source", "Source", "text", null, false, 0));
        var dto = await created.Content.ReadFromJsonAsync<FieldDefinitionDto>();

        var response = await ws.Client.DeleteAsync($"/api/field-definitions/{dto!.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var stillExists = await WithDb(db => db.FieldDefinitions.AnyAsync(f => f.Id == dto.Id));
        Assert.False(stillExists);
    }
}
