using System.Diagnostics;
using System.Text.Json;
using Anthropic.Models.Messages;
using CrmApi.Data;
using CrmApi.Observability;
using Microsoft.EntityFrameworkCore;

namespace CrmApi.Services;

public class SummarizationFailedException(string message) : Exception(message);

public record SummaryResult(string Summary, bool UsedCache);

// Summarize-on-read with incremental updates — see ai-features-architect:
// "long deal histories should get an incremental summary (summarize new
// activities + fold into prior summary) rather than re-summarizing
// everything each time, both for cost and latency." Cached on Deal.AiSummary;
// if nothing changed since the last summary, this returns the cache without
// calling Claude at all.
public class SummarizationService(AppDbContext db, IAnthropicMessagesClient anthropic, ILogger<SummarizationService> logger)
{
    private const string ModelId = "claude-opus-4-8";

    public async Task<SummaryResult> SummarizeDeal(string dealId, string workspaceId)
    {
        using var activity = CrmApiActivitySource.Instance.StartActivity("ai.summarization");
        activity?.SetTag("ai.model", ModelId);
        activity?.SetTag("workspace_id", workspaceId);
        activity?.SetTag("deal_id", dealId);

        var deal = await db.Deals
            .Include(d => d.Company)
            .FirstOrDefaultAsync(d => d.Id == dealId && d.WorkspaceId == workspaceId && d.DeletedAt == null);
        if (deal is null)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "deal not found");
            throw new SummarizationFailedException($"Deal {dealId} not found in workspace");
        }

        // Only activities since the last summary — the prior summary text
        // already accounts for everything before that.
        var newActivitiesQuery = db.Activities.Where(a => a.DealId == dealId);
        if (deal.AiSummarizedAt is { } summarizedAt)
            newActivitiesQuery = newActivitiesQuery.Where(a => a.CreatedAt > summarizedAt);
        var newActivities = await newActivitiesQuery.OrderBy(a => a.CreatedAt).ToListAsync();

        if (deal.AiSummarizedAt is not null && newActivities.Count == 0)
        {
            // Cache hit — nothing new to fold in, so no Claude call at all.
            activity?.SetStatus(ActivityStatusCode.Ok);
            activity?.SetTag("ai.cache_hit", true);
            logger.LogInformation("ai_call summarization cache_hit dealId={DealId} workspaceId={WorkspaceId}", dealId, workspaceId);
            return new SummaryResult(deal.AiSummary ?? "", UsedCache: true);
        }

        var context = new
        {
            dealTitle = deal.Company?.Name ?? "Untitled deal",
            priorSummary = deal.AiSummary,
            newActivities = newActivities.Select(a => new
            {
                type = a.Type,
                loggedAt = a.CreatedAt,
                body = a.Body != null && a.Body.Length > 400 ? a.Body[..400] : a.Body,
            }).ToList(),
        };

        var summaryTool = new Tool
        {
            Name = "record_deal_summary",
            Description = "Record an updated running summary of this deal's history, folding in the new activity described.",
            InputSchema = new()
            {
                Properties = new Dictionary<string, JsonElement>
                {
                    ["summary"] = JsonSerializer.SerializeToElement(new
                    {
                        type = "string",
                        description = "2-4 sentences a sales rep could read in a few seconds to catch up on this deal",
                    }),
                },
                Required = ["summary"],
            },
        };

        var startedAt = DateTime.UtcNow;
        Message response;
        try
        {
            response = await anthropic.Create(new MessageCreateParams
            {
                Model = ModelId,
                MaxTokens = 512,
                System = "You maintain a short running summary of a B2B sales deal for a sales rep, grounded only in the " +
                         "structured context provided. If a prior summary is given, update it to fold in the new activity " +
                         "rather than starting over — keep it to 2-4 sentences. Always call record_deal_summary.",
                Tools = [summaryTool],
                ToolChoice = new ToolChoiceTool { Name = "record_deal_summary" },
                Messages = [new() { Role = Role.User, Content = $"Update the summary based on this context:\n{JsonSerializer.Serialize(context)}" }],
            });
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            logger.LogError(ex, "ai_call summarization error dealId={DealId} workspaceId={WorkspaceId}", dealId, workspaceId);
            throw new SummarizationFailedException("Summarization is temporarily unavailable");
        }

        var toolUse = response.Content.Select(b => b.Value).OfType<ToolUseBlock>().FirstOrDefault();
        if (toolUse is null)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "no tool_use block");
            logger.LogWarning("ai_call summarization invalid_tool_output dealId={DealId}", dealId);
            throw new SummarizationFailedException("Model did not return a valid summary");
        }

        string summary;
        try
        {
            summary = toolUse.Input["summary"].GetString() ?? throw new JsonException("missing summary");
            if (string.IsNullOrWhiteSpace(summary)) throw new JsonException("empty summary");
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            logger.LogWarning(ex, "ai_call summarization invalid_tool_output dealId={DealId}", dealId);
            throw new SummarizationFailedException("Model did not return a valid summary");
        }

        deal.AiSummary = summary;
        deal.AiSummarizedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        activity?.SetStatus(ActivityStatusCode.Ok);
        activity?.SetTag("ai.cache_hit", false);
        activity?.SetTag("ai.input_tokens", response.Usage.InputTokens);
        activity?.SetTag("ai.output_tokens", response.Usage.OutputTokens);
        logger.LogInformation(
            "ai_call summarization succeeded dealId={DealId} workspaceId={WorkspaceId} newActivities={NewActivities} latencyMs={LatencyMs} inputTokens={InputTokens} outputTokens={OutputTokens}",
            dealId, workspaceId, newActivities.Count, (DateTime.UtcNow - startedAt).TotalMilliseconds, response.Usage.InputTokens, response.Usage.OutputTokens);

        return new SummaryResult(summary, UsedCache: false);
    }
}
