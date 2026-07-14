import { z } from "zod";
import type Anthropic from "@anthropic-ai/sdk";
import { db } from "@/lib/db";
import { anthropic, DEAL_SCORING_MODEL } from "@/lib/ai/anthropic";

// Structured tool-output contract — never parse a bare score out of prose.
// See .claude/skills/lead-deal-scoring and ai-tool-calling-pattern.
const ScoreResult = z.object({
  score: z.number().int().min(0).max(100),
  rationale: z.string(),
  signals: z.array(z.string()),
});
export type ScoreResult = z.infer<typeof ScoreResult>;

const SCORE_TOOL: Anthropic.Tool = {
  name: "record_deal_score",
  description:
    "Record the computed 0-100 score for this deal, a short rationale a sales rep would find useful, and the signals that drove the score.",
  input_schema: {
    type: "object",
    properties: {
      score: { type: "integer", minimum: 0, maximum: 100 },
      rationale: { type: "string", description: "1-2 sentences, references the actual signals" },
      signals: {
        type: "array",
        items: { type: "string" },
        description: "Short bullet-style factors that drove the score",
      },
    },
    required: ["score", "rationale", "signals"],
    additionalProperties: false,
  },
};

export class DealNotFoundError extends Error {}
export class ScoringFailedError extends Error {}

// Narrower than Anthropic["messages"] (which returns the SDK's APIPromise) so
// tests can inject a plain-Promise fake without fighting SDK-internal types —
// APIPromise is itself a Promise<Message>, so the real client satisfies this.
interface ScoringClient {
  create(params: Anthropic.MessageCreateParamsNonStreaming): Promise<Anthropic.Message>;
}

/**
 * Assembles the structured signals fed to the model — never lets the model
 * infer engagement recency from free text alone (lead-deal-scoring skill).
 */
async function assembleDealContext(dealId: string, workspaceId: string) {
  const deal = await db.deal.findFirst({
    where: { id: dealId, workspaceId, deletedAt: null },
    include: { stage: true, company: true, contacts: true },
  });
  if (!deal) throw new DealNotFoundError(`Deal ${dealId} not found in workspace`);

  const daysInStage = Math.floor(
    (Date.now() - deal.updatedAt.getTime()) / (1000 * 60 * 60 * 24),
  );

  return {
    deal,
    context: {
      amountCents: deal.amountCents,
      currency: deal.currency ?? "USD",
      stageName: deal.stage.name,
      stageProbability: deal.stage.probability,
      forecastCategory: deal.forecastCategory,
      daysInCurrentStage: daysInStage,
      companyName: deal.company?.name ?? null,
      contactCount: deal.contacts.length,
      contactLifecycleStages: deal.contacts.map((c) => c.lifecycleStage),
    },
  };
}

/**
 * Scores a deal via a forced tool call, validates the structured output, and
 * caches score/rationale/signals on the record. `client` is injectable so
 * tests can supply a fake — see src/lib/ai/score-deal.test.ts and
 * qa-test-engineer's "never call the real API in tests" guidance.
 */
export async function scoreDeal(
  dealId: string,
  workspaceId: string,
  client: ScoringClient = anthropic.messages,
): Promise<ScoreResult> {
  const { context } = await assembleDealContext(dealId, workspaceId);

  const startedAt = Date.now();
  let response: Anthropic.Message;
  try {
    response = await client.create({
      model: DEAL_SCORING_MODEL,
      max_tokens: 1024,
      system:
        "You score B2B sales deals 0-100 for how likely they are to close, based only on the structured signals provided. " +
        "0 means very unlikely to close soon; 100 means essentially certain. Always call record_deal_score.",
      tools: [SCORE_TOOL],
      tool_choice: { type: "tool", name: "record_deal_score" },
      messages: [
        {
          role: "user",
          content: `Score this deal based on these signals:\n${JSON.stringify(context, null, 2)}`,
        },
      ],
    });
  } catch (err) {
    // AI-call telemetry per observability-and-slo skill — outcome: error.
    console.error(
      JSON.stringify({
        event: "ai_call",
        feature: "deal_scoring",
        dealId,
        workspaceId,
        outcome: "error",
        latencyMs: Date.now() - startedAt,
        error: err instanceof Error ? err.message : String(err),
      }),
    );
    throw new ScoringFailedError("Deal scoring is temporarily unavailable");
  }

  const toolUse = response.content.find((block) => block.type === "tool_use");
  const parsed = toolUse ? ScoreResult.safeParse(toolUse.input) : undefined;

  console.log(
    JSON.stringify({
      event: "ai_call",
      feature: "deal_scoring",
      dealId,
      workspaceId,
      outcome: parsed?.success ? "success" : "invalid_tool_output",
      latencyMs: Date.now() - startedAt,
      inputTokens: response.usage.input_tokens,
      outputTokens: response.usage.output_tokens,
    }),
  );

  if (!parsed?.success) {
    throw new ScoringFailedError("Model did not return a valid score");
  }

  await db.deal.update({
    where: { id: dealId },
    data: {
      aiScore: parsed.data.score,
      aiScoreRationale: parsed.data.rationale,
      aiScoreSignals: parsed.data.signals,
      aiScoredAt: new Date(),
    },
  });

  return parsed.data;
}
