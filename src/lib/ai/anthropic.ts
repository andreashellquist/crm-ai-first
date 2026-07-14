import Anthropic from "@anthropic-ai/sdk";

const globalForAnthropic = globalThis as unknown as { anthropic?: Anthropic };

// Default per claude-api skill's model guidance. The lead-deal-scoring skill
// (.claude/skills/lead-deal-scoring) argues Haiku is normally sufficient for
// this specific high-volume, low-stakes call — revisit deliberately with the
// team rather than silently downgrading here.
export const DEAL_SCORING_MODEL = "claude-opus-4-8";

export const anthropic =
  globalForAnthropic.anthropic ??
  new Anthropic({ apiKey: process.env.ANTHROPIC_API_KEY });

if (process.env.NODE_ENV !== "production") globalForAnthropic.anthropic = anthropic;
