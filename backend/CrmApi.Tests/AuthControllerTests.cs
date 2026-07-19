using System.Net;
using System.Net.Http.Json;
using CrmApi.Dtos;
using CrmApi.Services;
using Microsoft.EntityFrameworkCore;

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

    [Fact]
    public async Task GoogleExchange_NewVerifiedUser_ProvisionsUserAndWorkspace()
    {
        var email = $"{Guid.NewGuid():N}@gmail.example";
        GoogleOAuth.NextUserInfo = new GoogleUserInfo("google-sub-1", email, true, "New Googler");

        var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/google/exchange", new GoogleExchangeRequest("fake-code"));

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body!.Token));
        Assert.Contains("New Googler", body.WorkspaceName);

        var user = await WithDb(db => db.Users.SingleAsync(u => u.Email == email));
        Assert.Equal("New Googler", user.Name);
        var membership = await WithDb(db => db.WorkspaceMembers.SingleAsync(m => m.UserId == user.Id));
        Assert.Equal("owner", membership.Role);
        Assert.Equal(body.WorkspaceId, membership.WorkspaceId);
    }

    [Fact]
    public async Task GoogleExchange_ExistingUser_LogsIntoExistingWorkspace()
    {
        var workspace = TestData.Workspace();
        var user = TestData.User();
        var member = TestData.Member(workspace, user, "admin");
        await WithDb(async db =>
        {
            db.Workspaces.Add(workspace);
            db.Users.Add(user);
            db.WorkspaceMembers.Add(member);
            await db.SaveChangesAsync();
        });
        GoogleOAuth.NextUserInfo = new GoogleUserInfo("google-sub-2", user.Email, true, "Existing User");

        var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/google/exchange", new GoogleExchangeRequest("fake-code"));

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.Equal(workspace.Id, body!.WorkspaceId);

        // No duplicate user/workspace created for a repeat sign-in.
        var userCount = await WithDb(db => db.Users.CountAsync(u => u.Email == user.Email));
        Assert.Equal(1, userCount);
    }

    [Fact]
    public async Task GoogleExchange_UnverifiedEmail_ReturnsUnauthorized()
    {
        GoogleOAuth.NextUserInfo = new GoogleUserInfo("google-sub-3", $"{Guid.NewGuid():N}@gmail.example", false, "Unverified");

        var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/google/exchange", new GoogleExchangeRequest("fake-code"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GoogleExchange_WhenGoogleCallFails_ReturnsUnauthorized()
    {
        GoogleOAuth.NextException = new InvalidOperationException("GoogleOAuth:ClientId/ClientSecret/RedirectUri are not configured");

        var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/google/exchange", new GoogleExchangeRequest("fake-code"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GoogleExchange_NewUser_ProvisionsAWorkingDefaultPipeline()
    {
        // Regression test for the gap this session closed: first-time OAuth
        // sign-in used to create a bare Workspace with no Pipeline/Stage
        // rows at all, which would break the pipeline board immediately.
        var email = $"{Guid.NewGuid():N}@gmail.example";
        GoogleOAuth.NextUserInfo = new GoogleUserInfo("google-sub-4", email, true, "Pipeline Checker");

        var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/google/exchange", new GoogleExchangeRequest("fake-code"));
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();

        var pipeline = await WithDb(db => db.Pipelines.SingleAsync(p => p.WorkspaceId == body!.WorkspaceId && p.IsDefault));
        var stageCount = await WithDb(db => db.Stages.CountAsync(s => s.PipelineId == pipeline.Id));
        Assert.Equal(6, stageCount);
        var settings = await WithDb(db => db.WorkspaceSettings.SingleAsync(s => s.WorkspaceId == body!.WorkspaceId));
        Assert.Equal("{}", settings.Terminology);
    }

    [Fact]
    public async Task Register_ValidRequest_CreatesUserAndProvisionsChosenTemplate()
    {
        var email = $"{Guid.NewGuid():N}@newco.example";
        var request = new RegisterRequest(email, "a-strong-password", "New Owner", "New Co", "real-estate");

        var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", request);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body!.Token));
        Assert.Equal("New Co", body.WorkspaceName);

        var user = await WithDb(db => db.Users.SingleAsync(u => u.Email == email));
        Assert.Equal("New Owner", user.Name);
        var membership = await WithDb(db => db.WorkspaceMembers.SingleAsync(m => m.UserId == user.Id));
        Assert.Equal("owner", membership.Role);

        var pipeline = await WithDb(db => db.Pipelines.SingleAsync(p => p.WorkspaceId == body.WorkspaceId && p.IsDefault));
        Assert.Equal("Listings Pipeline", pipeline.Name);
        var stageNames = await WithDb(db => db.Stages.Where(s => s.PipelineId == pipeline.Id).Select(s => s.Name).ToListAsync());
        Assert.Contains("Under Contract", stageNames);

        var settings = await WithDb(db => db.WorkspaceSettings.SingleAsync(s => s.WorkspaceId == body.WorkspaceId));
        Assert.Contains("Listing", settings.Terminology);

        var fieldKeys = await WithDb(db => db.FieldDefinitions
            .Where(f => f.WorkspaceId == body.WorkspaceId && f.EntityType == "deal")
            .Select(f => f.Key).ToListAsync());
        Assert.Contains("bedrooms", fieldKeys);
        Assert.Contains("mls_status", fieldKeys);
    }

    [Fact]
    public async Task Register_DuplicateEmail_ReturnsConflict()
    {
        var existing = TestData.User();
        await WithDb(async db =>
        {
            db.Users.Add(existing);
            await db.SaveChangesAsync();
        });

        var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(existing.Email, "a-strong-password", "Someone Else", "Some Workspace", VerticalTemplates.DefaultId));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Register_UnknownTemplate_ReturnsBadRequest()
    {
        var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest($"{Guid.NewGuid():N}@newco.example", "a-strong-password", "Name", "Workspace", "not-a-real-template"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_ShortPassword_ReturnsBadRequest()
    {
        var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest($"{Guid.NewGuid():N}@newco.example", "short", "Name", "Workspace", VerticalTemplates.DefaultId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Templates_ReturnsAllThreeVerticalTemplates()
    {
        var client = Factory.CreateClient();
        var response = await client.GetAsync("/api/auth/templates");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<List<VerticalTemplateSummaryDto>>();
        Assert.NotNull(body);
        Assert.Equal(3, body!.Count);
        var realEstate = body.Single(t => t.Id == "real-estate");
        Assert.Equal("Listing", realEstate.DealTerm);
        Assert.Equal("Listings", realEstate.DealTermPlural);
        var saasSales = body.Single(t => t.Id == VerticalTemplates.DefaultId);
        Assert.Equal("Deal", saasSales.DealTerm);
    }
}
