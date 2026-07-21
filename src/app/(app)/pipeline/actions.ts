"use server";

import { revalidatePath } from "next/cache";
import { requireWorkspace } from "@/lib/workspace";

export type CreateDealState = { error?: string };

export async function createDealAction(_prevState: CreateDealState, formData: FormData): Promise<CreateDealState> {
  const { api } = await requireWorkspace();

  const companyName = formData.get("companyName");
  if (typeof companyName !== "string" || !companyName.trim()) {
    return { error: "Company name is required" };
  }

  const stageId = formData.get("stageId");
  const amountDollars = formData.get("amount");
  const currency = formData.get("currency");

  const { error } = await api.POST("/api/deals", {
    body: {
      companyName,
      stageId: typeof stageId === "string" && stageId ? stageId : null,
      // The form collects whole-currency-unit amounts (e.g. "1000" meaning
      // $1,000) since that's what a rep actually types — cents is a
      // storage/API convention, not a UX one.
      amountCents: typeof amountDollars === "string" && amountDollars ? Math.round(Number(amountDollars) * 100) : null,
      currency: typeof currency === "string" && currency ? currency : null,
      forecastCategory: "pipeline",
      contactIds: null,
      customFields: null,
    },
  });

  if (error) {
    return { error: typeof error === "string" ? error : "Could not create deal" };
  }

  revalidatePath("/pipeline");
  return {};
}

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
