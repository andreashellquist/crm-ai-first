using CrmApi.Models;

namespace CrmApi.Tests;

// Convenience builders for integration tests. Every entity gets a fresh Guid
// id (the model defaults) so tests can run against a single shared database
// without colliding on rows from other tests.
public static class TestData
{
    public static Workspace Workspace(string? name = null) => new()
    {
        Name = name ?? $"Workspace {Guid.NewGuid():N}",
    };

    public static User User(string? email = null, string password = "password123") => new()
    {
        Email = email ?? $"{Guid.NewGuid():N}@test.local",
        PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
    };

    public static WorkspaceMember Member(Workspace workspace, User user, string role = "owner") => new()
    {
        WorkspaceId = workspace.Id,
        UserId = user.Id,
        Role = role,
    };

    public static Pipeline Pipeline(Workspace workspace, string name = "Default Pipeline", bool isDefault = true) => new()
    {
        WorkspaceId = workspace.Id,
        Name = name,
        IsDefault = isDefault,
    };

    public static Stage Stage(Pipeline pipeline, string name, int order, int probability = 50) => new()
    {
        PipelineId = pipeline.Id,
        Name = name,
        Order = order,
        Probability = probability,
    };

    public static Company Company(Workspace workspace, string name = "Acme Co") => new()
    {
        WorkspaceId = workspace.Id,
        Name = name,
    };

    public static Contact Contact(Workspace workspace, string firstName = "Jane", Company? company = null) => new()
    {
        WorkspaceId = workspace.Id,
        FirstName = firstName,
        CompanyId = company?.Id,
    };

    public static Deal Deal(Workspace workspace, Pipeline pipeline, Stage stage, Company? company = null, int? amountCents = 10_000_00) => new()
    {
        WorkspaceId = workspace.Id,
        PipelineId = pipeline.Id,
        StageId = stage.Id,
        CompanyId = company?.Id,
        AmountCents = amountCents,
        Currency = "USD",
    };

    public static Activity Activity(Workspace workspace, Deal deal, string type = "note", string body = "Test activity") => new()
    {
        WorkspaceId = workspace.Id,
        DealId = deal.Id,
        Type = type,
        Body = body,
    };
}
