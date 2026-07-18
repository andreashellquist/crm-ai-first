using System.Diagnostics;
using System.Text.Json;
using Anthropic.Models.Messages;
using CrmApi.Data;
using CrmApi.Observability;
using Microsoft.EntityFrameworkCore;

namespace CrmApi.Services;

public class DraftingFailedException(string message) : Exception(message);

public record EmailDraftResult(string Subject, string Body);

// Draft-and-review, never auto-sent — see ai-features-architect: "Always a
// draft in an editable compose box, never auto-sent." There is no send
// capability in this app yet (email/calendar integration is Phase 2 per
// docs/PRODUCT_SCOPE.md), so this can only ever produce a draft for a human
// to read/copy — the consent/suppression gate that would apply to an actual
// send doesn't apply here.
public class EmailDraftingService(AppDbContext db, IAnthropicMessagesClient anthropic, ILogger<EmailDraftingService> logger)
{
    private const string ModelId = "claude-opus-4-8";

    public async Task<EmailDraftResult> DraftEmail(string dealId, string workspaceId, string? instruction)
    {
        using var activity = CrmApiActivitySource.Instance.StartActivity("ai.email_drafting");
        activity?.SetTag("ai.model", ModelId);
        activity?.SetTag("workspace_id", workspaceId);
        activity?.SetTag("deal_id", dealId);

        var deal = await db.Deals
            .Include(d => d.Company)
            .Include(d => d.Contacts)
            .Include(d => d.Activities.OrderByDescending(a => a.CreatedAt).Take(8))
            .FirstOrDefaultAsync(d => d.Id == dealId && d.WorkspaceId == workspaceId && d.DeletedAt == null);
        if (deal is null)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "deal not found");
            throw new DraftingFailedException($"Deal {dealId} not found in workspace");
        }

        // Retrieval, not invention (ai-features-architect: "ground responses
        // in real CRM data") — the primary contact and recent activities are
        // the only source of facts the model is allowed to draw on.
        var contact = deal.Contacts.FirstOrDefault();
        var terminologyJson = await db.WorkspaceSettings
            .Where(s => s.WorkspaceId == workspaceId)
            .Select(s => s.Terminology)
            .FirstOrDefaultAsync();
        var dealTerm = TerminologyResolver.Resolve(terminologyJson, "deal", "deal");

        var context = new
        {
            dealTitle = deal.Company?.Name ?? "Untitled deal",
            contactFirstName = contact?.FirstName,
            contactLastName = contact?.LastName,
            userInstruction = instruction,
            recentActivities = deal.Activities.Select(a => new
            {
                type = a.Type,
                daysAgo = (int)(DateTime.UtcNow - a.CreatedAt).TotalDays,
                body = a.Body != null && a.Body.Length > 400 ? a.Body[..400] : a.Body,
            }).ToList(),
        };

        var draftTool = new Tool
        {
            Name = "record_email_draft",
            Description = $"Record a drafted follow-up email for this {dealTerm}'s primary contact.",
            InputSchema = new()
            {
                Properties = new Dictionary<string, JsonElement>
                {
                    ["subject"] = JsonSerializer.SerializeToElement(new { type = "string", description = "Short, specific subject line" }),
                    ["body"] = JsonSerializer.SerializeToElement(new { type = "string", description = "Plain-text email body, no signature block" }),
                },
                Required = ["subject", "body"],
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
                System = "You draft short, specific follow-up emails for a B2B sales rep, grounded only in the structured " +
                         "context provided — never invent facts, prices, or commitments not present in the activity history. " +
                         "If a user instruction is given, follow it. Always call record_email_draft.",
                Tools = [draftTool],
                ToolChoice = new ToolChoiceTool { Name = "record_email_draft" },
                Messages = [new() { Role = Role.User, Content = $"Draft a follow-up email based on this context:\n{JsonSerializer.Serialize(context)}" }],
            });
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            logger.LogError(ex, "ai_call email_drafting error dealId={DealId} workspaceId={WorkspaceId}", dealId, workspaceId);
            throw new DraftingFailedException("Email drafting is temporarily unavailable");
        }

        var toolUse = response.Content.Select(b => b.Value).OfType<ToolUseBlock>().FirstOrDefault();
        if (toolUse is null)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "no tool_use block");
            logger.LogWarning("ai_call email_drafting invalid_tool_output dealId={DealId}", dealId);
            throw new DraftingFailedException("Model did not return a valid draft");
        }

        EmailDraftResult result;
        try
        {
            var input = toolUse.Input;
            var subject = input["subject"].GetString() ?? throw new JsonException("missing subject");
            var body = input["body"].GetString() ?? throw new JsonException("missing body");
            if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(body))
                throw new JsonException("empty subject or body");
            result = new EmailDraftResult(subject, body);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            logger.LogWarning(ex, "ai_call email_drafting invalid_tool_output dealId={DealId}", dealId);
            throw new DraftingFailedException("Model did not return a valid draft");
        }

        activity?.SetStatus(ActivityStatusCode.Ok);
        activity?.SetTag("ai.input_tokens", response.Usage.InputTokens);
        activity?.SetTag("ai.output_tokens", response.Usage.OutputTokens);
        logger.LogInformation(
            "ai_call email_drafting succeeded dealId={DealId} workspaceId={WorkspaceId} latencyMs={LatencyMs} inputTokens={InputTokens} outputTokens={OutputTokens}",
            dealId, workspaceId, (DateTime.UtcNow - startedAt).TotalMilliseconds, response.Usage.InputTokens, response.Usage.OutputTokens);

        return result;
    }
}
