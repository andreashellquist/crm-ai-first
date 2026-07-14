"use server";

import { z } from "zod";
import { revalidatePath } from "next/cache";
import { db } from "@/lib/db";
import { requireWorkspace } from "@/lib/workspace";
import { enqueueJob, getJobStatus, type JobStatus } from "@/lib/jobs/queue";

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

export async function scoreDealAction(dealId: string): Promise<{ jobId: string }> {
  const { workspaceId } = await requireWorkspace();

  // Re-check the deal belongs to this workspace before enqueueing — the job
  // payload is trusted input to the worker process, so it must already be
  // scoped correctly by the time it leaves the request (database-schema-expert).
  const deal = await db.deal.findFirst({ where: { id: dealId, workspaceId } });
  if (!deal) throw new Error("Deal not found in this workspace");

  const jobId = await enqueueJob("score_deal", { dealId, workspaceId }, { workspaceId });
  return { jobId };
}

export type JobStatusResult = { status: JobStatus; lastError: string | null } | { status: "not_found" };

export async function getJobStatusAction(jobId: string): Promise<JobStatusResult> {
  const { workspaceId } = await requireWorkspace();
  const status = await getJobStatus(jobId, workspaceId);
  if (!status) return { status: "not_found" };
  return status;
}
