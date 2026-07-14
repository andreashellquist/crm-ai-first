import { registerHandler } from "@/lib/jobs/worker";
import { scoreDeal } from "@/lib/ai/score-deal";

interface ScoreDealPayload {
  dealId: string;
  workspaceId: string;
}

registerHandler("score_deal", async (payload) => {
  const { dealId, workspaceId } = payload as unknown as ScoreDealPayload;
  await scoreDeal(dealId, workspaceId);
});
