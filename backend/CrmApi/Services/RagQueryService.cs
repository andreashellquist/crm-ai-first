using System.Diagnostics;
using System.Text.Json;
using Anthropic.Models.Messages;
using CrmApi.Data;
using CrmApi.Observability;
using Microsoft.EntityFrameworkCore;

namespace CrmApi.Services;

public class RagQueryFailedException(string message) : Exception(message);

public record RagCitation(string ActivityId, string Type, string Snippet, DateTime CreatedAt, string? DealId, string? ContactId, string? CompanyId);

public record RagQueryResult(string Answer, List<RagCitation> Citations);

// RAG over CRM history — see ai-features-architect: "for cross-record
// questions, retrieve Activities scoped to workspace + relevant
// Company/Contact via a filtered vector or full-text search, not global
// semantic search across all workspaces." This uses ILIKE keyword matching,
// not a vector store or Postgres full-text search — the same v1 tradeoff
// SearchController already makes (database-schema-expert: "don't reach for
// FTS until ILIKE demonstrably can't keep up"), and this app has no
// embedding infrastructure to stand up speculatively.
public class RagQueryService(AppDbContext db, IAnthropicMessagesClient anthropic, ILogger<RagQueryService> logger)
{
    private const string ModelId = "claude-opus-4-8";
    private const int MaxActivities = 20;
    private const int SnippetLength = 300;

    // Short common words dropped from the question before it's used to
    // match Activity.Body / Company.Name / Contact first+last name — keeps
    // "what have we discussed with Acme about pricing" from matching every
    // activity in the workspace via "have"/"with"/"about".
    private static readonly HashSet<string> StopWords =
    [
        "a", "about", "an", "and", "any", "are", "as", "at", "be", "did", "do", "does", "for", "from", "had", "has",
        "have", "how", "in", "is", "it", "of", "on", "or", "our", "recent", "recently", "said", "say", "so", "that",
        "the", "their", "they", "this", "to", "us", "was", "we", "were", "what", "when", "where", "which", "who",
        "why", "with", "you", "your",
    ];

    public async Task<RagQueryResult> AskQuestion(string question, string workspaceId, string? dealId, string? contactId, string? companyId)
    {
        using var activity = CrmApiActivitySource.Instance.StartActivity("ai.rag_query");
        activity?.SetTag("ai.model", ModelId);
        activity?.SetTag("workspace_id", workspaceId);

        var terminologyJson = await db.WorkspaceSettings
            .Where(s => s.WorkspaceId == workspaceId)
            .Select(s => s.Terminology)
            .FirstOrDefaultAsync();
        var dealTerm = TerminologyResolver.Resolve(terminologyJson, "deal", "deal");
        var companyTerm = TerminologyResolver.Resolve(terminologyJson, "company", "company");

        var activities = await RetrieveActivities(question, workspaceId, dealId, contactId, companyId);

        var context = activities.Select(a => new
        {
            type = a.Type,
            loggedAt = a.CreatedAt,
            dealCompany = a.Deal?.Company?.Name,
            contact = a.Contact != null ? string.Join(" ", new[] { a.Contact.FirstName, a.Contact.LastName }.Where(s => !string.IsNullOrWhiteSpace(s))) : null,
            company = a.Company?.Name,
            body = a.Body != null && a.Body.Length > SnippetLength ? a.Body[..SnippetLength] : a.Body,
        }).ToList();

        var answerTool = new Tool
        {
            Name = "answer_question",
            Description = "Answer the user's question about their CRM data, grounded only in the activities provided as context.",
            InputSchema = new()
            {
                Properties = new Dictionary<string, JsonElement>
                {
                    ["answer"] = JsonSerializer.SerializeToElement(new
                    {
                        type = "string",
                        description = "A concise answer grounded only in the provided activities. If they don't contain " +
                                       "enough information to answer, say so plainly rather than guessing.",
                    }),
                    ["citedActivityIndexes"] = JsonSerializer.SerializeToElement(new
                    {
                        type = "array",
                        items = new { type = "integer" },
                        description = "0-based indexes into the provided activities array that support the answer. Empty if none do.",
                    }),
                },
                Required = ["answer", "citedActivityIndexes"],
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
                System = $"You answer a sales rep's questions about their own CRM data, grounded only in the " +
                          $"structured activity context provided — never invent {dealTerm}s, {companyTerm}s, or facts not " +
                          "present in that context. If the context is empty or doesn't cover the question, say so. Always call answer_question.",
                Tools = [answerTool],
                ToolChoice = new ToolChoiceTool { Name = "answer_question" },
                Messages = [new()
                {
                    Role = Role.User,
                    Content = $"Question: {question}\n\nActivities (0-indexed):\n{JsonSerializer.Serialize(context)}",
                }],
            });
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            logger.LogError(ex, "ai_call rag_query error workspaceId={WorkspaceId}", workspaceId);
            throw new RagQueryFailedException("Ask AI is temporarily unavailable");
        }

        var toolUse = response.Content.Select(b => b.Value).OfType<ToolUseBlock>().FirstOrDefault();
        if (toolUse is null)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "no tool_use block");
            logger.LogWarning("ai_call rag_query invalid_tool_output workspaceId={WorkspaceId}", workspaceId);
            throw new RagQueryFailedException("Model did not return a valid answer");
        }

        string answer;
        List<int> citedIndexes;
        try
        {
            answer = toolUse.Input["answer"].GetString() ?? throw new JsonException("missing answer");
            citedIndexes = toolUse.Input.TryGetValue("citedActivityIndexes", out var indexesEl)
                ? indexesEl.EnumerateArray().Select(e => e.GetInt32()).ToList()
                : [];
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            logger.LogWarning(ex, "ai_call rag_query invalid_tool_output workspaceId={WorkspaceId}", workspaceId);
            throw new RagQueryFailedException("Model did not return a valid answer");
        }

        var citations = citedIndexes
            .Where(i => i >= 0 && i < activities.Count)
            .Select(i => activities[i])
            .Select(a => new RagCitation(
                a.Id,
                a.Type,
                a.Body != null && a.Body.Length > SnippetLength ? a.Body[..SnippetLength] : a.Body ?? "",
                a.CreatedAt,
                a.DealId,
                a.ContactId,
                a.CompanyId))
            .ToList();

        activity?.SetStatus(ActivityStatusCode.Ok);
        activity?.SetTag("ai.input_tokens", response.Usage.InputTokens);
        activity?.SetTag("ai.output_tokens", response.Usage.OutputTokens);
        activity?.SetTag("rag.activities_retrieved", activities.Count);
        activity?.SetTag("rag.citations", citations.Count);
        logger.LogInformation(
            "ai_call rag_query succeeded workspaceId={WorkspaceId} activitiesRetrieved={ActivitiesRetrieved} citations={Citations} " +
            "latencyMs={LatencyMs} inputTokens={InputTokens} outputTokens={OutputTokens}",
            workspaceId, activities.Count, citations.Count, (DateTime.UtcNow - startedAt).TotalMilliseconds,
            response.Usage.InputTokens, response.Usage.OutputTokens);

        return new RagQueryResult(answer, citations);
    }

    private async Task<List<Models.Activity>> RetrieveActivities(string question, string workspaceId, string? dealId, string? contactId, string? companyId)
    {
        var query = db.Activities
            .Include(a => a.Deal!).ThenInclude(d => d.Company)
            .Include(a => a.Contact)
            .Include(a => a.Company)
            .Where(a => a.WorkspaceId == workspaceId);

        // A question asked from a specific record's page is scoped to that
        // record exactly — no keyword matching needed or wanted.
        if (dealId is not null) query = query.Where(a => a.DealId == dealId);
        else if (contactId is not null) query = query.Where(a => a.ContactId == contactId);
        else if (companyId is not null) query = query.Where(a => a.CompanyId == companyId);
        else
        {
            var keywords = question
                .Split(' ', '\t', '\n', ',', '.', '?', '!', ':', ';')
                .Select(w => w.Trim())
                .Where(w => w.Length >= 3 && !StopWords.Contains(w.ToLowerInvariant()))
                .Distinct()
                .ToList();

            if (keywords.Count > 0)
            {
                // Patterns are built client-side before the query — composing
                // $"%{k}%" directly inside the Any() lambda below fails to
                // translate (EF can't push a string.Format bound to a
                // closure-captured loop variable through ILIKE).
                var patterns = keywords.Select(k => $"%{k}%").ToList();
                query = query.Where(a =>
                    patterns.Any(p => EF.Functions.ILike(a.Body ?? "", p))
                    || patterns.Any(p => EF.Functions.ILike(a.Deal!.Company!.Name ?? "", p))
                    || patterns.Any(p => EF.Functions.ILike(a.Company!.Name ?? "", p))
                    || patterns.Any(p => EF.Functions.ILike(a.Contact!.FirstName ?? "", p))
                    || patterns.Any(p => EF.Functions.ILike(a.Contact!.LastName ?? "", p)));
            }
            // No usable keywords (e.g. a question that's all stop words) —
            // fall back to the workspace's most recent activity, better than
            // an empty/unfiltered scan of the whole table.
        }

        return await query.OrderByDescending(a => a.CreatedAt).Take(MaxActivities).ToListAsync();
    }
}
