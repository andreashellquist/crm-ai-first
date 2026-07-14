---
name: ai-tool-calling-pattern
description: Standard pattern for defining a Claude tool that lets an AI agent take a side-effecting action in this CRM (update a deal stage, create a task, send a drafted email). Load this when implementing any new AI-invocable action, so every tool follows the same Zod-validated, workspace-scoped, audit-logged shape instead of ad hoc parsing of model output.
---

# AI tool-calling pattern

Every side-effecting action an AI agent can take in this CRM is defined as a
Claude tool with a Zod schema, never inferred by parsing free-text model output.
See the `ai-features-architect` agent for the broader design principles this
pattern implements.

## Shape of a tool definition

```ts
import { z } from "zod";

const UpdateDealStageInput = z.object({
  dealId: z.string(),
  stageId: z.string(),
  reasoning: z.string().describe("Brief justification shown to the user for review"),
});

export const updateDealStageTool = {
  name: "update_deal_stage",
  description: "Move a deal to a different pipeline stage.",
  input_schema: zodToJsonSchema(UpdateDealStageInput),
  // handler runs server-side only, never trusts the model's dealId/stageId
  // without re-checking workspace scope
  async handler(rawInput: unknown, ctx: { workspaceId: string; actorId: string | null }) {
    const input = UpdateDealStageInput.parse(rawInput);

    const deal = await db.deal.findFirst({
      where: { id: input.dealId, workspaceId: ctx.workspaceId },
    });
    if (!deal) throw new ToolError("not_found", "Deal not found in this workspace");

    const stage = await db.stage.findFirst({
      where: { id: input.stageId, pipeline: { workspaceId: ctx.workspaceId } },
    });
    if (!stage) throw new ToolError("not_found", "Stage not found in this workspace");

    await db.deal.update({ where: { id: deal.id }, data: { stageId: stage.id } });

    await db.auditEvent.create({
      data: {
        workspaceId: ctx.workspaceId,
        actorId: ctx.actorId, // null when the AI acted autonomously
        entityType: "Deal",
        entityId: deal.id,
        action: "stage_changed_by_ai",
        metadata: { fromStageId: deal.stageId, toStageId: stage.id, reasoning: input.reasoning },
      },
    });

    return { ok: true, dealId: deal.id, newStageId: stage.id };
  },
};
```

## Rules

1. **Re-validate scope inside the handler.** Never trust that an ID the model
   passed belongs to the current workspace — look it up scoped by `workspaceId`
   and treat a miss as `not_found`, not a silent no-op or a cross-tenant write.
2. **Autonomous vs. review-gated is a property of the *call site*, not the tool.**
   The same `update_deal_stage` tool can run immediately in an autonomous flow
   the user opted into, or be surfaced as a "propose" step requiring explicit
   confirmation before the handler ever runs — decide this per workflow, not per
   tool, and default to review-gated for anything irreversible-feeling (stage
   changes, deletes, sends) per `ai-features-architect`.
3. **Every tool call that mutates data writes an audit event**, with `actorId`
   null when the AI acted without a human triggering it, so the UI can render
   "AI changed this" distinctly from a user action (see `frontend-engineer` for
   the distinct-treatment UI convention).
4. **Read-only tools (get_deal, list_activities, search_contacts) still scope by
   workspaceId** — an agent's retrieval step is exactly where a tenant-isolation
   bug would leak another workspace's data into a response.
5. **Return structured results, not prose**, so the calling code (and the UI) can
   render outcomes deterministically instead of re-parsing model text.
