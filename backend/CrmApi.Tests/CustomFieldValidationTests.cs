using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CrmApi.Dtos;

namespace CrmApi.Tests;

// Exercises CustomFieldValidator end-to-end through the one existing entry
// point that accepts customFields today — ContactsController.Create. See
// the workspace-customization skill.
[Collection(CrmApiCollection.Name)]
public class CustomFieldValidationTests(CrmApiFactory factory) : IntegrationTestBase(factory)
{
    private Task<HttpResponseMessage> DefineField(WorkspaceScenario ws, string key, string fieldType, bool required = false, List<string>? options = null) =>
        ws.Client.PostAsJsonAsync("/api/field-definitions", new CreateFieldDefinitionRequest("contact", key, key, fieldType, options, required, 0));

    [Fact]
    public async Task Create_WithValidCustomField_Persists()
    {
        var ws = await SeedWorkspaceAsync();
        (await DefineField(ws, "budget", "number")).EnsureSuccessStatusCode();

        var response = await ws.Client.PostAsJsonAsync("/api/contacts", new CreateContactRequest(
            "Jane", null, null, null,
            new Dictionary<string, JsonElement> { ["budget"] = JsonSerializer.SerializeToElement(50000) }));

        response.EnsureSuccessStatusCode();
        var contact = await response.Content.ReadFromJsonAsync<ContactDto>();
        Assert.Equal(50000, contact!.CustomFields["budget"].GetInt32());
    }

    [Fact]
    public async Task Create_MissingRequiredCustomField_ReturnsBadRequest()
    {
        var ws = await SeedWorkspaceAsync();
        (await DefineField(ws, "budget", "number", required: true)).EnsureSuccessStatusCode();

        var response = await ws.Client.PostAsJsonAsync("/api/contacts", new CreateContactRequest("Jane", null, null, null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WrongTypeForCustomField_ReturnsBadRequest()
    {
        var ws = await SeedWorkspaceAsync();
        (await DefineField(ws, "budget", "number")).EnsureSuccessStatusCode();

        var response = await ws.Client.PostAsJsonAsync("/api/contacts", new CreateContactRequest(
            "Jane", null, null, null,
            new Dictionary<string, JsonElement> { ["budget"] = JsonSerializer.SerializeToElement("a lot") }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_UnknownCustomFieldKey_ReturnsBadRequest()
    {
        var ws = await SeedWorkspaceAsync();

        var response = await ws.Client.PostAsJsonAsync("/api/contacts", new CreateContactRequest(
            "Jane", null, null, null,
            new Dictionary<string, JsonElement> { ["not_a_real_field"] = JsonSerializer.SerializeToElement("x") }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_SelectFieldWithInvalidOption_ReturnsBadRequest()
    {
        var ws = await SeedWorkspaceAsync();
        (await DefineField(ws, "source", "select", options: ["referral", "outbound"])).EnsureSuccessStatusCode();

        var response = await ws.Client.PostAsJsonAsync("/api/contacts", new CreateContactRequest(
            "Jane", null, null, null,
            new Dictionary<string, JsonElement> { ["source"] = JsonSerializer.SerializeToElement("made-up") }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_SelectFieldWithValidOption_Persists()
    {
        var ws = await SeedWorkspaceAsync();
        (await DefineField(ws, "source", "select", options: ["referral", "outbound"])).EnsureSuccessStatusCode();

        var response = await ws.Client.PostAsJsonAsync("/api/contacts", new CreateContactRequest(
            "Jane", null, null, null,
            new Dictionary<string, JsonElement> { ["source"] = JsonSerializer.SerializeToElement("referral") }));

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Create_CustomFieldsAreIsolatedPerWorkspace()
    {
        var ws1 = await SeedWorkspaceAsync();
        var ws2 = await SeedWorkspaceAsync();
        (await DefineField(ws1, "budget", "number")).EnsureSuccessStatusCode();

        // ws2 never defined "budget" — supplying it should be rejected as unknown.
        var response = await ws2.Client.PostAsJsonAsync("/api/contacts", new CreateContactRequest(
            "Jane", null, null, null,
            new Dictionary<string, JsonElement> { ["budget"] = JsonSerializer.SerializeToElement(1) }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
