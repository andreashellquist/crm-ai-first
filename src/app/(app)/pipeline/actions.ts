"use server";

import { revalidatePath } from "next/cache";
import { requireWorkspace } from "@/lib/workspace";

export async function moveDealStageAction(input: { dealId: string; stageId: string }) {
  const { api } = await requireWorkspace();
  const { error } = await api.POST("/api/deals/{dealId}/move", {
    params: { path: { dealId: input.dealId } },
    body: { stageId: input.stageId },
  });
  if (error) throw new Error("Deal or stage not found in this workspace");
  revalidatePath("/pipeline");
}

export async function scoreDealAction(dealId: string): Promise<{ jobId: string }> {
  const { api } = await requireWorkspace();
  const { data, error } = await api.POST("/api/deals/{dealId}/score", {
    params: { path: { dealId } },
  });
  if (error || !data) throw new Error("Deal not found in this workspace");
  return { jobId: data.jobId };
}

export type JobStatusResult =
  | { status: "pending" | "processing" | "succeeded" | "failed"; lastError: string | null }
  | { status: "not_found" };

export async function getJobStatusAction(jobId: string): Promise<JobStatusResult> {
  const { api } = await requireWorkspace();
  const { data, response } = await api.GET("/api/jobs/{jobId}", {
    params: { path: { jobId } },
  });
  if (response.status === 404 || !data) return { status: "not_found" };
  return { status: data.status as "pending" | "processing" | "succeeded" | "failed", lastError: data.lastError ?? null };
}
