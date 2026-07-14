"use server";

import { z } from "zod";
import { revalidatePath } from "next/cache";
import { db } from "@/lib/db";
import { requireWorkspace } from "@/lib/workspace";

const moveDealSchema = z.object({
  dealId: z.string(),
  stageId: z.string(),
});

export async function moveDealStageAction(input: { dealId: string; stageId: string }) {
  const { workspaceId } = await requireWorkspace();
  const { dealId, stageId } = moveDealSchema.parse(input);

  // Re-validate both IDs belong to this workspace before writing — never trust
  // that a client-supplied ID is already scoped correctly (database-schema-expert).
  const [deal, stage] = await Promise.all([
    db.deal.findFirst({ where: { id: dealId, workspaceId } }),
    db.stage.findFirst({ where: { id: stageId, pipeline: { workspaceId } } }),
  ]);
  if (!deal || !stage) throw new Error("Deal or stage not found in this workspace");

  await db.deal.update({ where: { id: deal.id }, data: { stageId: stage.id } });
  revalidatePath("/pipeline");
}
