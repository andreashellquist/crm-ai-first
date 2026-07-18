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
    // Tests share this fake across the whole collection (sequential, but
    // never reset) — a monotonic counter lets a "the model was NOT called
    // during this test" assertion snapshot-and-compare instead of relying on
    // LastRequest being null, which would break the moment any earlier test
    // in the run had already called it.
    public int CallCount { get; private set; }

    public Task<Message> Create(MessageCreateParams parameters)
    {
        LastRequest = parameters;
        CallCount++;

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

    public static Message DefaultDraftMessage(string subject = "Fake subject", string body = "Fake draft body for testing.") =>
        ToolUseMessage("record_email_draft", new JsonObject
        {
            ["subject"] = subject,
            ["body"] = body,
        });

    public static Message DefaultSummaryMessage(string summary = "Fake summary for testing.") =>
        ToolUseMessage("record_deal_summary", new JsonObject { ["summary"] = summary });

    // A read-only lookup tool call (get_deal_details/list_recent_activities/
    // list_contacts) with no input — used to exercise NextBestActionService's
    // multi-turn loop before the model calls the final suggest_actions tool.
    public static Message ReadOnlyToolCallMessage(string toolName) =>
        ToolUseMessage(toolName, new JsonObject());

    public static Message DefaultSuggestActionsMessage(params (string Action, string Reasoning, string Confidence)[] suggestions)
    {
        var items = suggestions.Length > 0
            ? suggestions
            : [("Send a follow-up email", "No recent contact in the last week.", "medium")];
        return ToolUseMessage("suggest_actions", new JsonObject
        {
            ["suggestions"] = new JsonArray(items.Select(s => (JsonNode)new JsonObject
            {
                ["action"] = s.Action,
                ["reasoning"] = s.Reasoning,
                ["confidence"] = s.Confidence,
            }).ToArray()),
        });
    }

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
