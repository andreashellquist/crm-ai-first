using System.Net;
using System.Net.Http.Json;
using CrmApi.Dtos;
using CrmApi.Services;
using Microsoft.EntityFrameworkCore;

namespace CrmApi.Tests;

[Collection(CrmApiCollection.Name)]
public class SsoControllerTests(CrmApiFactory factory) : IntegrationTestBase(factory)
{
    private static UpdateSsoConnectionRequest ValidRequest(string? domain = null) =>
        new("https://idp.example.com", "client-123", "secret-abc", domain ?? $"{Guid.NewGuid():N}.example", Enforced: false, IsActive: true);

    [Fact]
    public async Task Get_WithNoConnection_ReturnsNull()
    {
        var ws = await SeedWorkspaceAsync();

        var response = await ws.Client.GetAsync("/api/workspace/sso");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(string.IsNullOrEmpty(body) || body == "null");
    }

    [Fact]
    public async Task Update_AsOwner_PersistsConnectionAndNeverEchoesSecret()
    {
        var ws = await SeedWorkspaceAsync();
        var domain = $"{Guid.NewGuid():N}.example";

        var response = await ws.Client.PutAsJsonAsync("/api/workspace/sso", ValidRequest(domain));

        response.EnsureSuccessStatusCode();
        var dto = await response.Content.ReadFromJsonAsync<SsoConnectionDto>();
        Assert.NotNull(dto);
        Assert.Equal(domain, dto!.EmailDomain);
        Assert.Equal("https://idp.example.com", dto.Issuer);
        Assert.DoesNotContain("secret-abc", await response.Content.ReadAsStringAsync());

        var refetched = await ws.Client.GetFromJsonAsync<SsoConnectionDto>("/api/workspace/sso");
        Assert.Equal(domain, refetched!.EmailDomain);
    }

    [Fact]
    public async Task Update_AsMember_ReturnsForbidden()
    {
        var ws = await SeedWorkspaceAsync();
        var memberClient = AuthedClient(ws.User.Id, ws.Workspace.Id, "member");

        var response = await memberClient.PutAsJsonAsync("/api/workspace/sso", ValidRequest());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("not-a-url", "acme.example")]
    [InlineData("http://idp.example.com", "acme.example")] // not https
    [InlineData("https://idp.example.com", "jane@acme.example")] // full email, not a domain
    [InlineData("https://idp.example.com", "no-dot")]
    public async Task Update_WithInvalidInput_ReturnsBadRequest(string issuer, string domain)
    {
        var ws = await SeedWorkspaceAsync();

        var response = await ws.Client.PutAsJsonAsync("/api/workspace/sso",
            new UpdateSsoConnectionRequest(issuer, "client-123", "secret-abc", domain, false, true));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_DomainAlreadyClaimedByAnotherWorkspace_ReturnsConflict()
    {
        var ws1 = await SeedWorkspaceAsync();
        var ws2 = await SeedWorkspaceAsync();
        var domain = $"{Guid.NewGuid():N}.example";
        (await ws1.Client.PutAsJsonAsync("/api/workspace/sso", ValidRequest(domain))).EnsureSuccessStatusCode();

        var response = await ws2.Client.PutAsJsonAsync("/api/workspace/sso", ValidRequest(domain));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Delete_RemovesConnection()
    {
        var ws = await SeedWorkspaceAsync();
        (await ws.Client.PutAsJsonAsync("/api/workspace/sso", ValidRequest())).EnsureSuccessStatusCode();

        var response = await ws.Client.DeleteAsync("/api/workspace/sso");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var refetched = await ws.Client.GetAsync("/api/workspace/sso");
        var body = await refetched.Content.ReadAsStringAsync();
        Assert.True(string.IsNullOrEmpty(body) || body == "null");
    }

    [Fact]
    public async Task Start_UnknownDomain_ReturnsFoundFalse()
    {
        var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/sso/start",
            new SsoStartRequest($"user@{Guid.NewGuid():N}-nope.example", "https://app.example.com/callback"));

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<SsoStartResponse>();
        Assert.False(body!.Found);
        Assert.Null(body.AuthorizationUrl);
    }

    [Fact]
    public async Task Start_KnownActiveDomain_ReturnsAuthorizationUrlAndState()
    {
        var ws = await SeedWorkspaceAsync();
        var domain = $"{Guid.NewGuid():N}.example";
        (await ws.Client.PutAsJsonAsync("/api/workspace/sso", ValidRequest(domain))).EnsureSuccessStatusCode();

        var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/sso/start",
            new SsoStartRequest($"jane@{domain}", "https://app.example.com/callback"));

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<SsoStartResponse>();
        Assert.True(body!.Found);
        Assert.NotNull(body.AuthorizationUrl);
        Assert.Contains("client_id=client-123", body.AuthorizationUrl);
        Assert.Contains("state=", body.AuthorizationUrl);
        Assert.NotNull(body.State);
    }

    private async Task<(WorkspaceScenario Ws, string Domain)> SeedConnectionAsync(bool enforced = false)
    {
        var ws = await SeedWorkspaceAsync();
        var domain = $"{Guid.NewGuid():N}.example";
        (await ws.Client.PutAsJsonAsync("/api/workspace/sso",
            new UpdateSsoConnectionRequest("https://idp.example.com", "client-123", "secret-abc", domain, enforced, true)))
            .EnsureSuccessStatusCode();
        return (ws, domain);
    }

    private async Task<string> StartAndGetStateAsync(HttpClient client, string email)
    {
        var start = await client.PostAsJsonAsync("/api/auth/sso/start", new SsoStartRequest(email, "https://app.example.com/callback"));
        var body = await start.Content.ReadFromJsonAsync<SsoStartResponse>();
        return body!.State!;
    }

    [Fact]
    public async Task Exchange_NewUser_JitProvisionsIntoTheConnectionsWorkspace()
    {
        var (ws, domain) = await SeedConnectionAsync();
        var email = $"newperson@{domain}";
        Oidc.NextUserInfo = new OidcUserInfo("oidc-sub-1", email, true, "New Person");
        var client = Factory.CreateClient();
        var state = await StartAndGetStateAsync(client, email);

        var response = await client.PostAsJsonAsync("/api/auth/sso/exchange",
            new SsoExchangeRequest(state, "fake-code", "https://app.example.com/callback"));

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.Equal(ws.Workspace.Id, body!.WorkspaceId);

        var user = await WithDb(db => db.Users.SingleAsync(u => u.Email == email));
        var membership = await WithDb(db => db.WorkspaceMembers.SingleAsync(m => m.UserId == user.Id && m.WorkspaceId == ws.Workspace.Id));
        Assert.Equal("member", membership.Role);
    }

    [Fact]
    public async Task Exchange_ExistingUserNewToWorkspace_JitProvisionsMembershipOnly()
    {
        var (ws, domain) = await SeedConnectionAsync();
        var otherWorkspace = await SeedWorkspaceAsync();
        var existingUser = TestData.User(email: $"shared@{domain}");
        await WithDb(async db =>
        {
            db.Users.Add(existingUser);
            db.WorkspaceMembers.Add(TestData.Member(otherWorkspace.Workspace, existingUser, "member"));
            await db.SaveChangesAsync();
        });
        Oidc.NextUserInfo = new OidcUserInfo("oidc-sub-2", existingUser.Email, true, "Shared Person");
        var client = Factory.CreateClient();
        var state = await StartAndGetStateAsync(client, existingUser.Email);

        var response = await client.PostAsJsonAsync("/api/auth/sso/exchange",
            new SsoExchangeRequest(state, "fake-code", "https://app.example.com/callback"));

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.Equal(ws.Workspace.Id, body!.WorkspaceId);

        var userCount = await WithDb(db => db.Users.CountAsync(u => u.Email == existingUser.Email));
        Assert.Equal(1, userCount); // no duplicate user
        var membershipCount = await WithDb(db => db.WorkspaceMembers.CountAsync(m => m.UserId == existingUser.Id));
        Assert.Equal(2, membershipCount); // original + newly JIT-provisioned
    }

    [Fact]
    public async Task Exchange_UnverifiedEmail_ReturnsUnauthorized()
    {
        var (_, domain) = await SeedConnectionAsync();
        var email = $"unverified@{domain}";
        Oidc.NextUserInfo = new OidcUserInfo("oidc-sub-3", email, false, "Unverified");
        var client = Factory.CreateClient();
        var state = await StartAndGetStateAsync(client, email);

        var response = await client.PostAsJsonAsync("/api/auth/sso/exchange",
            new SsoExchangeRequest(state, "fake-code", "https://app.example.com/callback"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Exchange_EmailDomainMismatch_ReturnsUnauthorized()
    {
        var (_, domain) = await SeedConnectionAsync();
        // IdP claims a different email domain than the connection's — a
        // misconfigured/compromised IdP response, not something the state
        // parameter alone should let through.
        Oidc.NextUserInfo = new OidcUserInfo("oidc-sub-4", "someone@totally-different.example", true, "Mismatch");
        var client = Factory.CreateClient();
        var state = await StartAndGetStateAsync(client, $"jane@{domain}");

        var response = await client.PostAsJsonAsync("/api/auth/sso/exchange",
            new SsoExchangeRequest(state, "fake-code", "https://app.example.com/callback"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Exchange_WhenProviderCallFails_ReturnsUnauthorized()
    {
        var (_, domain) = await SeedConnectionAsync();
        var client = Factory.CreateClient();
        var state = await StartAndGetStateAsync(client, $"jane@{domain}");
        Oidc.NextExchangeException = new HttpRequestException("token endpoint unreachable");

        var response = await client.PostAsJsonAsync("/api/auth/sso/exchange",
            new SsoExchangeRequest(state, "fake-code", "https://app.example.com/callback"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Exchange_WithMalformedState_ReturnsUnauthorized()
    {
        var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/sso/exchange",
            new SsoExchangeRequest("", "fake-code", "https://app.example.com/callback"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_WhenSsoEnforcedForMatchingDomain_ReturnsForbidden()
    {
        var (ws, domain) = await SeedConnectionAsync(enforced: true);
        var user = TestData.User(email: $"jane@{domain}", password: "correct-password");
        await WithDb(async db =>
        {
            db.Users.Add(user);
            db.WorkspaceMembers.Add(TestData.Member(ws.Workspace, user, "member"));
            await db.SaveChangesAsync();
        });

        var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email, "correct-password"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Login_WhenSsoConfiguredButNotEnforced_PasswordLoginStillWorks()
    {
        var (ws, domain) = await SeedConnectionAsync(enforced: false);
        var user = TestData.User(email: $"jane@{domain}", password: "correct-password");
        await WithDb(async db =>
        {
            db.Users.Add(user);
            db.WorkspaceMembers.Add(TestData.Member(ws.Workspace, user, "member"));
            await db.SaveChangesAsync();
        });

        var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email, "correct-password"));

        response.EnsureSuccessStatusCode();
    }
}
