using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic.Models.Messages;
using CrmApi.Services;

namespace CrmApi.Tests;

// Stands in for the real Anthropic client in every test — see
// qa-test-engineer: "never call the real Claude API in unit/CI tests."
// Defaults to a canned successful `record_deal_score` tool call; tests that
// need a different response set NextResponse/NextException before calling
// the endpoint under test.
//
// Messages are built from raw JSON matching the real API response shape
// (constructed via JsonSerializer.Deserialize<Message>) rather than an
// object initializer — Message/Usage/ToolUseBlock declare several
// `required` members (StopDetails, ServiceTier, Caller, ...) that only the
// SDK's own JSON converter knows how to populate sensibly; deserializing a
// minimal JSON payload is the supported way to get a valid instance.
public class FakeAnthropicMessagesClient : IAnthropicMessagesClient
{
    public Func<MessageCreateParams, Message>? NextResponse { get; set; }
    public Exception? NextException { get; set; }
    public MessageCreateParams? LastRequest { get; private set; }

    public Task<Message> Create(MessageCreateParams parameters)
    {
        LastRequest = parameters;

        if (NextException is { } ex)
        {
            NextException = null;
            throw ex;
        }

        var message = NextResponse?.Invoke(parameters) ?? DefaultScoreMessage();
        return Task.FromResult(message);
    }

    public static Message DefaultScoreMessage(int score = 72, string rationale = "Fake rationale for testing.") =>
        ToolUseMessage("record_deal_score", new JsonObject
        {
            ["score"] = score,
            ["rationale"] = rationale,
            ["signals"] = new JsonArray("fake signal one", "fake signal two"),
        });

    public static Message ToolUseMessage(string toolName, JsonObject input)
    {
        var payload = new JsonObject
        {
            ["id"] = "msg_fake",
            ["type"] = "message",
            ["role"] = "assistant",
            ["model"] = "claude-test-fake",
            ["content"] = new JsonArray(new JsonObject
            {
                ["type"] = "tool_use",
                ["id"] = "toolu_fake",
                ["name"] = toolName,
                ["input"] = input,
            }),
            ["stop_reason"] = "tool_use",
            ["stop_sequence"] = null,
            ["usage"] = new JsonObject { ["input_tokens"] = 100, ["output_tokens"] = 50 },
        };
        return JsonSerializer.Deserialize<Message>(payload.ToJsonString())!;
    }

    // A response with no tool_use block at all — exercises the "model
    // didn't call the tool" validation path in DealScoringService.
    public static Message TextOnlyMessage(string text = "I have thoughts but no tool call.")
    {
        var payload = new JsonObject
        {
            ["id"] = "msg_fake",
            ["type"] = "message",
            ["role"] = "assistant",
            ["model"] = "claude-test-fake",
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
            ["stop_reason"] = "end_turn",
            ["stop_sequence"] = null,
            ["usage"] = new JsonObject { ["input_tokens"] = 100, ["output_tokens"] = 50 },
        };
        return JsonSerializer.Deserialize<Message>(payload.ToJsonString())!;
    }
}
