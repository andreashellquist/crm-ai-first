import { afterAll, beforeAll, describe, expect, it } from "vitest";
import type Anthropic from "@anthropic-ai/sdk";
import { db } from "@/lib/db";
import { scoreDeal, DealNotFoundError, ScoringFailedError } from "@/lib/ai/score-deal";

// Per qa-test-engineer: never call the real Claude API in tests — inject a
// fake client that returns a fixed tool-use response. This is otherwise an
// integration test (real Postgres) for the signal-assembly + tool-output
// contract, which is exactly what needs a real DB to catch workspace-scoping
// bugs (see auth-security-expert's isolation-test requirement).
function fakeClient(toolInput: unknown): { create: () => Promise<Anthropic.Message> } {
  return {
    create: async () =>
      ({
        id: "msg_fake",
        type: "message",
        role: "assistant",
        model: "claude-opus-4-8",
        content: [
          {
            type: "tool_use",
            id: "toolu_fake",
            name: "record_deal_score",
            input: toolInput,
          },
        ],
        stop_reason: "tool_use",
        stop_sequence: null,
        usage: { input_tokens: 10, output_tokens: 5 },
      }) as unknown as Anthropic.Message,
  };
}

describe("scoreDeal", () => {
  let workspaceId: string;
  let otherWorkspaceId: string;
  let dealId: string;

  beforeAll(async () => {
    const workspace = await db.workspace.create({ data: { name: "Score Test Workspace" } });
    workspaceId = workspace.id;

    const otherWorkspace = await db.workspace.create({ data: { name: "Other Workspace" } });
    otherWorkspaceId = otherWorkspace.id;

    const pipeline = await db.pipeline.create({
      data: {
        workspaceId,
        name: "Test Pipeline",
        stages: { create: [{ name: "Qualified", order: 0, probability: 30 }] },
      },
      include: { stages: true },
    });

    const deal = await db.deal.create({
      data: {
        workspaceId,
        pipelineId: pipeline.id,
        stageId: pipeline.stages[0].id,
        amountCents: 500000,
      },
    });
    dealId = deal.id;
  });

  afterAll(async () => {
    await db.workspace.delete({ where: { id: workspaceId } });
    await db.workspace.delete({ where: { id: otherWorkspaceId } });
  });

  it("validates the tool output and caches it on the deal", async () => {
    const client = fakeClient({
      score: 72,
      rationale: "Large deal, stalled a while in Qualified.",
      signals: ["amount above workspace average", "12 days in current stage"],
    });

    const result = await scoreDeal(dealId, workspaceId, client);

    expect(result.score).toBe(72);
    expect(result.signals).toHaveLength(2);

    const updated = await db.deal.findUniqueOrThrow({ where: { id: dealId } });
    expect(updated.aiScore).toBe(72);
    expect(updated.aiScoreRationale).toContain("stalled");
    expect(updated.aiScoredAt).not.toBeNull();
  });

  it("throws DealNotFoundError for a deal outside the given workspace", async () => {
    const client = fakeClient({ score: 50, rationale: "n/a", signals: [] });
    await expect(scoreDeal(dealId, otherWorkspaceId, client)).rejects.toBeInstanceOf(
      DealNotFoundError,
    );
  });

  it("throws ScoringFailedError and does not write to the deal on invalid tool output", async () => {
    const client = fakeClient({ score: "not-a-number", rationale: "bad" }); // fails Zod: missing signals, wrong type

    await expect(scoreDeal(dealId, workspaceId, client)).rejects.toBeInstanceOf(
      ScoringFailedError,
    );

    const unchanged = await db.deal.findUniqueOrThrow({ where: { id: dealId } });
    expect(unchanged.aiScore).toBe(72); // still the value from the first test, not overwritten
  });
});
