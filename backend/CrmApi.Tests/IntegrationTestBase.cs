using System.Net.Http.Headers;
using CrmApi.Data;
using CrmApi.Models;
using CrmApi.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrmApi.Tests;

public record WorkspaceScenario(Workspace Workspace, User User, Pipeline Pipeline, Stage StageOne, Stage StageTwo, HttpClient Client);

// Concrete test classes must additionally declare [Collection(CrmApiCollection.Name)]
// themselves — xUnit does not walk base classes for collection membership.
public abstract class IntegrationTestBase(CrmApiFactory factory)
{
    protected CrmApiFactory Factory { get; } = factory;
    protected FakeAnthropicMessagesClient Anthropic => Factory.Anthropic;
    protected FakeGoogleOAuthClient GoogleOAuth => Factory.GoogleOAuth;

    protected Task WithDb(Func<AppDbContext, Task> action) => WithDb(async db =>
    {
        await action(db);
        return 0;
    });

    protected async Task<T> WithDb<T>(Func<AppDbContext, Task<T>> action)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await action(db);
    }

    // Drives JobWorker directly instead of waiting on its 2s idle-poll loop
    // (disabled entirely in the test host — see CrmApiFactory). Loops until
    // the queue is drained: tests that don't care about a job's outcome
    // (e.g. a multi-tenant isolation check that only enqueues incidentally)
    // should call this so it doesn't linger and get scooped up — along with
    // whatever fake Anthropic response happens to be configured at the
    // time — by a later test's batch.
    protected async Task<int> ProcessAllPendingJobsAsync()
    {
        var worker = new JobWorker(Factory.Services.GetRequiredService<IServiceScopeFactory>(), Factory.Services.GetRequiredService<ILogger<JobWorker>>());
        var total = 0;
        int claimed;
        do
        {
            claimed = await worker.ProcessBatchAsync(CancellationToken.None);
            total += claimed;
        } while (claimed > 0);
        return total;
    }

    protected string TokenFor(string userId, string workspaceId, string role = "member") =>
        Factory.Jwt.GenerateToken(userId, workspaceId, role);

    protected HttpClient AuthedClient(string userId, string workspaceId, string role = "member")
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenFor(userId, workspaceId, role));
        return client;
    }

    // Public v1 API auth (ApiKeyAuthenticationHandler) uses X-Api-Key, not a
    // Bearer token — a real raw key, generated/hashed the same way
    // ApiKeysController.Create does, inserted directly so tests don't need
    // to go through the issuing endpoint just to exercise v1 endpoints.
    protected async Task<string> CreateApiKeyAsync(string workspaceId, string userId, params string[] scopes)
    {
        var (rawKey, hashedKey) = ApiKeyGenerator.Generate();
        await WithDb(async db =>
        {
            db.ApiKeys.Add(new ApiKey
            {
                WorkspaceId = workspaceId,
                Name = "Test key",
                HashedKey = hashedKey,
                Scopes = scopes.ToList(),
                CreatedByUserId = userId,
            });
            await db.SaveChangesAsync();
        });
        return rawKey;
    }

    protected HttpClient ApiKeyClient(string rawKey)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", rawKey);
        return client;
    }

    // Seeds a workspace with an owner user and a two-stage default pipeline —
    // the minimum every controller test needs — and returns an authed
    // HttpClient for that user alongside the ids tests typically assert on.
    protected async Task<WorkspaceScenario> SeedWorkspaceAsync(string? workspaceName = null)
    {
        var workspace = TestData.Workspace(workspaceName);
        var user = TestData.User();
        var member = TestData.Member(workspace, user);
        var pipeline = TestData.Pipeline(workspace);
        var stageOne = TestData.Stage(pipeline, "New", 0, 10);
        var stageTwo = TestData.Stage(pipeline, "Won", 1, 100);

        await WithDb(async db =>
        {
            db.Workspaces.Add(workspace);
            db.Users.Add(user);
            db.WorkspaceMembers.Add(member);
            db.Pipelines.Add(pipeline);
            db.Stages.AddRange(stageOne, stageTwo);
            await db.SaveChangesAsync();
        });

        return new WorkspaceScenario(workspace, user, pipeline, stageOne, stageTwo, AuthedClient(user.Id, workspace.Id, member.Role));
    }
}
