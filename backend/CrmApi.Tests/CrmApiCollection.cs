namespace CrmApi.Tests;

// All test classes share this collection so xUnit runs them sequentially
// against the one CrmApiFactory instance — the shared test database and the
// mutable FakeAnthropicMessagesClient are not safe under parallel execution.
[CollectionDefinition(Name)]
public class CrmApiCollection : ICollectionFixture<CrmApiFactory>
{
    public const string Name = "CrmApi";
}
