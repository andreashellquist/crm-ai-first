using System.Diagnostics;
using System.Text.Json;
using Anthropic.Models.Messages;
using CrmApi.Data;
using CrmApi.Observability;
using Microsoft.EntityFrameworkCore;

namespace CrmApi.Services;

public class NextBestActionFailedException(string message) : Exception(message);

public record NextBestActionSuggestion(string Action, string Reasoning, string Confidence);

public record NextBestActionResult(List<NextBestActionSuggestion> Suggestions);

// Agentic, read-only tool-use loop — see ai-features-architect: "next-best-action
// suggestions are read-only proposals; executing one is a separate, explicit
// human-confirmed step, never triggered directly from this loop." The model is
// given zero-parameter lookup tools (no model-supplied IDs, ever — every query
// is scoped by closing over dealId/workspaceId) so it can gather whatever
// context it judges relevant before calling the final suggest_actions tool.
// Manual loop per the claude-api skill (no Tool Runner): reconstruct each
// ContentBlock as its *Param counterpart, one tool_result per tool_use_id,
// loop until suggest_actions is called or MaxTurns is hit.
public class NextBestActionService(AppDbContext db, IAnthropicMessagesClient anthropic, ILogger<NextBestActionService> logger)
{
    private const string ModelId = "claude-opus-4-8";
    private const int MaxTurns = 6;
    private static readonly string[] ValidConfidence = ["low", "medium", "high"];

    public async Task<NextBestActionResult> SuggestActions(string dealId, string workspaceId)
    {
        using var activity = CrmApiActivitySource.Instance.StartActivity("ai.next_best_action");
        activity?.SetTag("ai.model", ModelId);
        activity?.SetTag("workspace_id", workspaceId);
        activity?.SetTag("deal_id", dealId);

        var dealExists = await db.Deals.AnyAsync(d => d.Id == dealId && d.WorkspaceId == workspaceId && d.DeletedAt == null);
        if (!dealExists)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "deal not found");
            throw new NextBestActionFailedException($"Deal {dealId} not found in workspace");
        }

        var terminologyJson = await db.WorkspaceSettings
            .Where(s => s.WorkspaceId == workspaceId)
            .Select(s => s.Terminology)
            .FirstOrDefaultAsync();
        var dealTerm = TerminologyResolver.Resolve(terminologyJson, "deal", "deal");

        Tool[] readOnlyTools =
        [
            new Tool
            {
                Name = "get_deal_details",
                Description = $"Get this {dealTerm}'s stage, amount, forecast category, and any existing AI score/summary.",
                InputSchema = new() { Properties = new Dictionary<string, JsonElement>() },
            },
            new Tool
            {
                Name = "list_recent_activities",
                Description = "List the most recent calls, emails, meetings, and notes logged against this deal.",
                InputSchema = new() { Properties = new Dictionary<string, JsonElement>() },
            },
            new Tool
            {
                Name = "list_contacts",
                Description = "List the contacts associated with this deal, including lifecycle stage.",
                InputSchema = new() { Properties = new Dictionary<string, JsonElement>() },
            },
        ];
        var finalTool = new Tool
        {
            Name = "suggest_actions",
            Description = $"Record 1-4 concrete next-best actions a sales rep should consider taking on this {dealTerm}. " +
                           "Call this once you have gathered enough context from the other tools.",
            InputSchema = new()
            {
                Properties = new Dictionary<string, JsonElement>
                {
                    ["suggestions"] = JsonSerializer.SerializeToElement(new
                    {
                        type = "array",
                        description = "1-4 suggested next actions, ordered most to least impactful",
                        items = new
                        {
                            type = "object",
                            properties = new
                            {
                                action = new { type = "string", description = "A short, concrete action, e.g. 'Send a pricing follow-up email'" },
                                reasoning = new { type = "string", description = "Why this action makes sense given the deal's current state" },
                                confidence = new { type = "string", @enum = ValidConfidence },
                            },
                            required = new[] { "action", "reasoning", "confidence" },
                        },
                    }),
                },
                Required = ["suggestions"],
            },
        };

        var parameters = new MessageCreateParams
        {
            Model = ModelId,
            MaxTokens = 1024,
            System = $"You are a sales assistant deciding the next-best actions for a B2B {dealTerm}. Use the read-only " +
                     "lookup tools to gather whatever context you need — you do not have to call all of them, and you may " +
                     "call them in any order or skip straight to a decision if the deal is simple. Ground every suggestion " +
                     "only in data returned by the tools, never invent facts. When ready, call suggest_actions exactly once.",
            Tools = [.. readOnlyTools, finalTool],
            Messages = [new() { Role = Role.User, Content = $"What should a sales rep do next on this {dealTerm}?" }],
        };

        var startedAt = DateTime.UtcNow;
        for (var turn = 0; turn < MaxTurns; turn++)
        {
            Message response;
            try
            {
                response = await anthropic.Create(parameters);
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                logger.LogError(ex, "ai_call next_best_action error dealId={DealId} workspaceId={WorkspaceId}", dealId, workspaceId);
                throw new NextBestActionFailedException("Next-best-action is temporarily unavailable");
            }

            var toolUseBlocks = response.Content.Select(b => b.Value).OfType<ToolUseBlock>().ToList();
            var finalCall = toolUseBlocks.FirstOrDefault(t => t.Name == "suggest_actions");
            if (finalCall is not null)
            {
                var result = ParseSuggestions(finalCall);
                activity?.SetStatus(ActivityStatusCode.Ok);
                activity?.SetTag("ai.turns", turn + 1);
                activity?.SetTag("ai.input_tokens", response.Usage.InputTokens);
                activity?.SetTag("ai.output_tokens", response.Usage.OutputTokens);
                logger.LogInformation(
                    "ai_call next_best_action succeeded dealId={DealId} workspaceId={WorkspaceId} turns={Turns} latencyMs={LatencyMs} inputTokens={InputTokens} outputTokens={OutputTokens}",
                    dealId, workspaceId, turn + 1, (DateTime.UtcNow - startedAt).TotalMilliseconds, response.Usage.InputTokens, response.Usage.OutputTokens);
                return result;
            }

            // Reconstruct the assistant turn per the claude-api skill's manual-loop
            // pattern — no .ToParam() helper exists, so each ContentBlock variant
            // is rebuilt as its *Param counterpart.
            List<ContentBlockParam> assistantContent = [];
            List<ContentBlockParam> toolResults = [];
            foreach (var block in response.Content)
            {
                if (block.TryPickText(out TextBlock? text))
                {
                    assistantContent.Add(new TextBlockParam { Text = text.Text });
                }
                else if (block.TryPickToolUse(out ToolUseBlock? toolUse))
                {
                    assistantContent.Add(new ToolUseBlockParam { ID = toolUse.ID, Name = toolUse.Name, Input = toolUse.Input });
                    var resultJson = await ExecuteReadOnlyTool(toolUse.Name, dealId, workspaceId);
                    toolResults.Add(new ToolResultBlockParam { ToolUseID = toolUse.ID, Content = resultJson });
                }
            }

            if (toolUseBlocks.Count == 0)
            {
                // Text-only turn, no tool call — nudge it back on track rather
                // than failing outright.
                parameters = parameters with
                {
                    Messages =
                    [
                        .. parameters.Messages,
                        new() { Role = Role.Assistant, Content = assistantContent },
                        new() { Role = Role.User, Content = "Please call one of the provided tools." },
                    ],
                };
                continue;
            }

            parameters = parameters with
            {
                Messages =
                [
                    .. parameters.Messages,
                    new() { Role = Role.Assistant, Content = assistantContent },
                    new() { Role = Role.User, Content = toolResults },
                ],
            };
        }

        activity?.SetStatus(ActivityStatusCode.Error, "max turns exceeded");
        logger.LogWarning("ai_call next_best_action max_turns_exceeded dealId={DealId} workspaceId={WorkspaceId}", dealId, workspaceId);
        throw new NextBestActionFailedException("Could not determine next-best actions — try again");
    }

    private async Task<string> ExecuteReadOnlyTool(string name, string dealId, string workspaceId)
    {
        // Every query below is re-scoped by dealId + workspaceId regardless
        // of tool name — these tools take no model-supplied input, so there
        // is no ID for the model to substitute another workspace's data in.
        switch (name)
        {
            case "get_deal_details":
            {
                var deal = await db.Deals
                    .Include(d => d.Stage)
                    .Include(d => d.Company)
                    .Where(d => d.Id == dealId && d.WorkspaceId == workspaceId && d.DeletedAt == null)
                    .Select(d => new
                    {
                        companyName = d.Company != null ? d.Company.Name : null,
                        stageName = d.Stage!.Name,
                        amountCents = d.AmountCents,
                        currency = d.Currency,
                        forecastCategory = d.ForecastCategory,
                        aiScore = d.AiScore,
                        aiScoreRationale = d.AiScoreRationale,
                        aiSummary = d.AiSummary,
                        createdAt = d.CreatedAt,
                        closedAt = d.ClosedAt,
                    })
                    .FirstOrDefaultAsync();
                return JsonSerializer.Serialize(deal);
            }
            case "list_recent_activities":
            {
                var activities = await db.Activities
                    .Where(a => a.DealId == dealId && a.WorkspaceId == workspaceId)
                    .OrderByDescending(a => a.CreatedAt)
                    .Take(15)
                    .Select(a => new
                    {
                        type = a.Type,
                        loggedAt = a.CreatedAt,
                        body = a.Body != null && a.Body.Length > 400 ? a.Body.Substring(0, 400) : a.Body,
                    })
                    .ToListAsync();
                return JsonSerializer.Serialize(activities);
            }
            case "list_contacts":
            {
                var contacts = await db.Deals
                    .Where(d => d.Id == dealId && d.WorkspaceId == workspaceId)
                    .SelectMany(d => d.Contacts)
                    .Select(c => new
                    {
                        firstName = c.FirstName,
                        lastName = c.LastName,
                        email = c.Email,
                        phone = c.Phone,
                        lifecycleStage = c.LifecycleStage,
                    })
                    .ToListAsync();
                return JsonSerializer.Serialize(contacts);
            }
            default:
                return JsonSerializer.Serialize(new { error = $"Unknown tool \"{name}\"" });
        }
    }

    private static NextBestActionResult ParseSuggestions(ToolUseBlock toolUse)
    {
        try
        {
            var suggestions = toolUse.Input["suggestions"].EnumerateArray()
                .Select(s =>
                {
                    var action = s.GetProperty("action").GetString() ?? throw new JsonException("missing action");
                    var reasoning = s.GetProperty("reasoning").GetString() ?? throw new JsonException("missing reasoning");
                    var confidence = s.GetProperty("confidence").GetString() ?? throw new JsonException("missing confidence");
                    if (string.IsNullOrWhiteSpace(action) || string.IsNullOrWhiteSpace(reasoning))
                        throw new JsonException("empty action or reasoning");
                    if (!ValidConfidence.Contains(confidence))
                        throw new JsonException($"invalid confidence \"{confidence}\"");
                    return new NextBestActionSuggestion(action, reasoning, confidence);
                })
                .ToList();
            if (suggestions.Count == 0) throw new JsonException("no suggestions");
            return new NextBestActionResult(suggestions);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new NextBestActionFailedException("Model did not return valid suggestions");
        }
    }
}
