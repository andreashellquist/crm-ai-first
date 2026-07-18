using Anthropic.Models.Messages;

namespace CrmApi.Services;

// Thin seam around the SDK's Messages.Create call so tests can inject a fake
// instead of hitting the real API — mirrors the TypeScript version's
// injectable `client` parameter on scoreDeal(). See qa-test-engineer:
// "Never call the real Claude API in unit/CI tests."
public interface IAnthropicMessagesClient
{
    Task<Message> Create(MessageCreateParams parameters);
}
