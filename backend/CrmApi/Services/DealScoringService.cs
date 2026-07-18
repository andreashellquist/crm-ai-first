using System.Text.Json;
using Anthropic.Models.Messages;
using CrmApi.Data;
using Microsoft.EntityFrameworkCore;

namespace CrmApi.Services;

public class DealNotFoundException(string message) : Exception(message);

public class ScoringFailedException(string message) : Exception(message);

public record ScoreResult(int Score, string Rationale, List<string> Signals);

// Structured tool-output contract — never parse a bare score out of prose.
// See .claude/skills/lead-deal-scoring and ai-tool-calling-pattern.
// `anthropic` is injected as IAnthropicMessagesClient so tests can supply a
// fake — see CrmApi.Tests/Fakes/FakeAnthropicMessagesClient.cs.
public class DealScoringService(AppDbContext db, IAnthropicMessagesClient anthropic, ILogger<DealScoringService> logger)
{
    // Default per the claude-api skill. The lead-deal-scoring skill argues
    // Haiku is normally sufficient for this specific high-volume, low-stakes
    // call — revisit deliberately with the team rather than silently
    // downgrading here (carried over verbatim from the TypeScript version).
    private const string ModelId = "claude-opus-4-8";

    public async Task<ScoreResult> ScoreDeal(string dealId, string workspaceId)
    {
        var deal = await db.Deals
            .Include(d => d.Stage)
            .Include(d => d.Company)
            .Include(d => d.Contacts)
            .Include(d => d.Activities.OrderByDescending(a => a.CreatedAt).Take(5))
            .FirstOrDefaultAsync(d => d.Id == dealId && d.WorkspaceId == workspaceId && d.DeletedAt == null);
        if (deal is null) throw new DealNotFoundException($"Deal {dealId} not found in workspace");

        var daysInStage = (int)(DateTime.UtcNow - deal.UpdatedAt).TotalDays;
        var signalContext = new
        {
            amountCents = deal.AmountCents,
            currency = deal.Currency ?? "USD",
            stageName = deal.Stage!.Name,
            stageProbability = deal.Stage.Probability,
            forecastCategory = deal.ForecastCategory,
            daysInCurrentStage = daysInStage,
            companyName = deal.Company?.Name,
            contactCount = deal.Contacts.Count,
            contactLifecycleStages = deal.Contacts.Select(c => c.LifecycleStage).ToList(),
            // Last few Activities, truncated — lets the rationale reference
            // what actually happened, not just the numeric signals.
            recentActivities = deal.Activities.Select(a => new
            {
                type = a.Type,
                daysAgo = (int)(DateTime.UtcNow - a.CreatedAt).TotalDays,
                body = a.Body != null && a.Body.Length > 300 ? a.Body[..300] : a.Body,
            }).ToList(),
        };

        var scoreTool = new Tool
        {
            Name = "record_deal_score",
            Description =
                "Record the computed 0-100 score for this deal, a short rationale a sales rep would find useful, and the signals that drove the score.",
            InputSchema = new()
            {
                Properties = new Dictionary<string, JsonElement>
                {
                    ["score"] = JsonSerializer.SerializeToElement(new { type = "integer", minimum = 0, maximum = 100 }),
                    ["rationale"] = JsonSerializer.SerializeToElement(new { type = "string", description = "1-2 sentences, references the actual signals" }),
                    ["signals"] = JsonSerializer.SerializeToElement(new
                    {
                        type = "array",
                        items = new { type = "string" },
                        description = "Short bullet-style factors that drove the score",
                    }),
                },
                Required = ["score", "rationale", "signals"],
            },
        };

        var startedAt = DateTime.UtcNow;
        Message response;
        try
        {
            response = await anthropic.Create(new MessageCreateParams
            {
                Model = ModelId,
                MaxTokens = 1024,
                System = "You score B2B sales deals 0-100 for how likely they are to close, based only on the structured signals provided. " +
                         "0 means very unlikely to close soon; 100 means essentially certain. Always call record_deal_score.",
                Tools = [scoreTool],
                ToolChoice = new ToolChoiceTool { Name = "record_deal_score" },
                Messages = [new() { Role = Role.User, Content = $"Score this deal based on these signals:\n{JsonSerializer.Serialize(signalContext)}" }],
            });
        }
        catch (Exception ex)
        {
            // AI-call telemetry per observability-and-slo skill — outcome: error.
            logger.LogError(ex, "ai_call deal_scoring error dealId={DealId} workspaceId={WorkspaceId}", dealId, workspaceId);
            throw new ScoringFailedException("Deal scoring is temporarily unavailable");
        }

        var toolUse = response.Content.Select(b => b.Value).OfType<ToolUseBlock>().FirstOrDefault();
        if (toolUse is null)
        {
            logger.LogWarning("ai_call deal_scoring invalid_tool_output dealId={DealId}", dealId);
            throw new ScoringFailedException("Model did not return a valid score");
        }

        ScoreResult result;
        try
        {
            var input = toolUse.Input;
            var score = input["score"].GetInt32();
            var rationale = input["rationale"].GetString() ?? throw new JsonException("missing rationale");
            var signals = input["signals"].EnumerateArray().Select(s => s.GetString() ?? "").ToList();
            if (score is < 0 or > 100) throw new JsonException("score out of range");
            result = new ScoreResult(score, rationale, signals);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            logger.LogWarning(ex, "ai_call deal_scoring invalid_tool_output dealId={DealId}", dealId);
            throw new ScoringFailedException("Model did not return a valid score");
        }

        deal.AiScore = result.Score;
        deal.AiScoreRationale = result.Rationale;
        deal.AiScoreSignals = JsonSerializer.Serialize(result.Signals);
        deal.AiScoredAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        logger.LogInformation(
            "ai_call deal_scoring succeeded dealId={DealId} workspaceId={WorkspaceId} latencyMs={LatencyMs} inputTokens={InputTokens} outputTokens={OutputTokens}",
            dealId, workspaceId, (DateTime.UtcNow - startedAt).TotalMilliseconds, response.Usage.InputTokens, response.Usage.OutputTokens);

        return result;
    }
}
