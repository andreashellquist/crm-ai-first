using Anthropic;
using Anthropic.Models.Messages;

namespace CrmApi.Services;

// Real implementation of IAnthropicMessagesClient, wrapping the official SDK
// (NuGet package "Anthropic" — not "Anthropic.SDK", a different package).
public class AnthropicMessagesClient : IAnthropicMessagesClient
{
    private readonly AnthropicClient _client;

    public AnthropicMessagesClient(IConfiguration config)
    {
        var apiKey = config["Anthropic:ApiKey"];
        _client = string.IsNullOrWhiteSpace(apiKey) ? new AnthropicClient() : new AnthropicClient { ApiKey = apiKey };
    }

    public Task<Message> Create(MessageCreateParams parameters) => _client.Messages.Create(parameters);
}
