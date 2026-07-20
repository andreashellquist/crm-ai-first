using CrmApi.Data;
using CrmApi.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrmApi.Tests;

// Boots the real API pipeline (controllers, auth, EF Core) against a
// dedicated Postgres test database, with the real Anthropic client swapped
// for a fake — see qa-test-engineer: "never call the real API in tests."
// One database is shared across the whole test run; tests avoid collisions
// by giving every row a fresh Guid rather than resetting/truncating between
// tests (see TestData).
//
// Config comes from appsettings.Test.json (loaded automatically via
// UseEnvironment("Test")) rather than WebApplicationFactory's
// ConfigureAppConfiguration hook — Program.cs reads Jwt:Secret directly off
// WebApplicationBuilder.Configuration *before* WebApplicationFactory's
// deferred host-builder interception applies, so an in-memory override
// added there arrives too late and Program.cs throws. Environment
// variables (e.g. a CI-injected ConnectionStrings__DefaultConnection) are
// read at the same early point as the JSON files and correctly take
// precedence over them, so TEST_DATABASE_URL is forwarded that way below.
public class CrmApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public FakeAnthropicMessagesClient Anthropic { get; } = new();
    public FakeGoogleOAuthClient GoogleOAuth { get; } = new();
    public FakeOidcClient Oidc { get; } = new();
    public FakeWebhookHttpMessageHandler WebhookHandler { get; } = new();

    static CrmApiFactory()
    {
        var testDbUrl = Environment.GetEnvironmentVariable("TEST_DATABASE_URL");
        if (!string.IsNullOrWhiteSpace(testDbUrl))
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", testDbUrl);
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAnthropicMessagesClient>();
            services.AddSingleton<IAnthropicMessagesClient>(Anthropic);
            services.RemoveAll<IGoogleOAuthClient>();
            services.AddSingleton<IGoogleOAuthClient>(GoogleOAuth);
            services.RemoveAll<IOidcClient>();
            services.AddSingleton<IOidcClient>(Oidc);
            // Overrides Program.cs's "webhooks" named client's primary
            // handler — applied after Program.cs's own AddHttpClient
            // registration, which wins since HttpClientFactory applies
            // ConfigurePrimaryHttpMessageHandler registrations in order.
            services.AddHttpClient("webhooks").ConfigurePrimaryHttpMessageHandler(() => WebhookHandler);

            // JobWorker normally self-schedules on a 2s idle-poll loop as a
            // BackgroundService. Tests drive it deterministically instead by
            // constructing one and calling ProcessBatchAsync directly — if
            // the hosted-service copy also ran, both could race to claim the
            // same job (FOR UPDATE SKIP LOCKED prevents corruption, but not
            // the resulting test flakiness), so it's removed here.
            services.RemoveAll<Microsoft.Extensions.Hosting.IHostedService>();
        });
    }

    // xUnit calls this once before any test in the collection runs, applying
    // migrations to the test database.
    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
    }

    // Base WebApplicationFactory.Dispose() (called automatically by xUnit
    // since it's IDisposable) tears down the host — nothing extra to do here.
    Task IAsyncLifetime.DisposeAsync() => Task.CompletedTask;

    public JwtService Jwt
    {
        get
        {
            using var scope = Services.CreateScope();
            return scope.ServiceProvider.GetRequiredService<JwtService>();
        }
    }
}
