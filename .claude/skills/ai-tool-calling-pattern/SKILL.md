---
name: ai-tool-calling-pattern
description: Standard pattern for defining a Claude tool that lets an AI agent take a side-effecting action in this CRM (update a deal stage, create a task, send a drafted email), using the official Anthropic C# SDK in backend/CrmApi. Load this when implementing any new AI-invocable action, so every tool follows the same validated, workspace-scoped shape instead of ad hoc parsing of model output.
---

# AI tool-calling pattern

Every side-effecting action an AI agent can take in this CRM is defined as a
Claude tool with an explicit JSON schema, never inferred by parsing free-text
model output. See the `ai-features-architect` agent for the broader design
principles this pattern implements, and `DealScoringService.cs` for a working,
verified reference (a read/analysis tool rather than a side-effecting one, but
the same tool-definition and response-parsing shape applies).

**Before writing Claude API code**: load the `claude-api` skill and read its
`csharp/claude-api/` reference rather than guessing bindings — the official
NuGet package is `Anthropic` (**not** `Anthropic.SDK`, a different, unofficial
package pulled in by mistake once already in this project's history).

## Shape of a tool definition (C#)

```csharp
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;

var updateStageTool = new Tool
{
    Name = "update_deal_stage",
    Description = "Move a deal to a different pipeline stage.",
    InputSchema = new()
    {
        Properties = new Dictionary<string, JsonElement>
        {
            ["dealId"] = JsonSerializer.SerializeToElement(new { type = "string" }),
            ["stageId"] = JsonSerializer.SerializeToElement(new { type = "string" }),
            ["reasoning"] = JsonSerializer.SerializeToElement(new
            {
                type = "string",
                description = "Brief justification shown to the user for review",
            }),
        },
        Required = ["dealId", "stageId", "reasoning"],
    },
};

var response = await client.Messages.Create(new MessageCreateParams
{
    Model = "claude-opus-4-8",
    MaxTokens = 1024,
    Tools = [updateStageTool],
    ToolChoice = new ToolChoiceTool { Name = "update_deal_stage" }, // force this tool, or omit for "auto"
    Messages = [new() { Role = Role.User, Content = "..." }],
});

var toolUse = response.Content.Select(b => b.Value).OfType<ToolUseBlock>().FirstOrDefault();
if (toolUse is null) throw new ScoringFailedException("Model did not call the tool");

// toolUse.Input is IReadOnlyDictionary<string, JsonElement> — index, don't
// call .GetProperty() (that's a JsonElement method, not available here).
var dealId = toolUse.Input["dealId"].GetString()!;
var stageId = toolUse.Input["stageId"].GetString()!;
var reasoning = toolUse.Input["reasoning"].GetString()!;

// Handler runs server-side only, never trusts the model's dealId/stageId
// without re-checking workspace scope — same discipline as any controller
// action (backend-api-engineer).
var deal = await db.Deals.FirstOrDefaultAsync(d => d.Id == dealId && d.WorkspaceId == workspaceId);
if (deal is null) throw new DealNotFoundException("Deal not found in this workspace");

var stage = await db.Stages.Include(s => s.Pipeline)
    .FirstOrDefaultAsync(s => s.Id == stageId && s.Pipeline!.WorkspaceId == workspaceId);
if (stage is null) throw new DealNotFoundException("Stage not found in this workspace");

deal.StageId = stage.Id;
deal.UpdatedAt = DateTime.UtcNow;
await db.SaveChangesAsync();

// Audit event — actorId null when the AI acted without a human triggering it.
db.AuditEvents.Add(new AuditEvent
{
    WorkspaceId = workspaceId,
    ActorId = actorId, // null for autonomous AI actions
    EntityType = "Deal",
    EntityId = deal.Id,
    Action = "stage_changed_by_ai",
    Metadata = JsonSerializer.Serialize(new { fromStageId = deal.StageId, toStageId = stage.Id, reasoning }),
});
await db.SaveChangesAsync();
```

## Rules

1. **Re-validate scope inside the handler.** Never trust that an ID the model
   passed belongs to the current workspace — look it up scoped by
   `WorkspaceId` and treat a miss as not-found, not a silent no-op or a
   cross-tenant write.
2. **Autonomous vs. review-gated is a property of the *call site*, not the
   tool.** The same `update_deal_stage` tool can run immediately in an
   autonomous flow the user opted into, or be surfaced as a "propose" step
   requiring explicit confirmation before the handler ever runs — decide this
   per workflow, not per tool, and default to review-gated for anything
   irreversible-feeling (stage changes, deletes, sends) per
   `ai-features-architect`.
3. **Every tool call that mutates data writes an audit event** (`AuditEvent`
   — not yet built, see `database-schema-expert`), with `ActorId` null when
   the AI acted without a human triggering it, so the UI can render "AI
   changed this" distinctly from a user action (see `frontend-engineer` for
   the distinct-treatment UI convention).
4. **Read-only tools (get_deal, list_activities, search_contacts) still scope
   by `WorkspaceId`** — an agent's retrieval step is exactly where a
   tenant-isolation bug would leak another workspace's data into a response.
5. **Return structured results, not prose**, so the calling code (and the UI)
   can render outcomes deterministically instead of re-parsing model text.
   `DealScoringService`'s Zod-equivalent is manual `JsonElement` field access
   plus range/null checks — wrap it in a try/catch that converts any parse
   failure into your feature's own "invalid model output" exception (see
   `ScoringFailedException`), never let a malformed tool call surface as an
   unhandled 500.
