"use server";

import { revalidatePath } from "next/cache";
import { requireWorkspace } from "@/lib/workspace";

export type LogActivityState = { error?: string };

export async function logActivityAction(
  _prevState: LogActivityState,
  formData: FormData,
): Promise<LogActivityState> {
  const { api } = await requireWorkspace();

  const dealId = formData.get("dealId");
  const type = formData.get("type");
  const body = formData.get("body");
  if (typeof dealId !== "string" || typeof type !== "string" || typeof body !== "string" || body.trim() === "") {
    return { error: "Enter some notes" };
  }

  // Workspace/deal-ownership re-check now happens server-side in the .NET
  // API (LogActivity re-scopes by workspace before writing).
  const { error } = await api.POST("/api/deals/{dealId}/activities", {
    params: { path: { dealId } },
    body: { type, body },
  });
  if (error) return { error: "Could not log activity" };

  revalidatePath(`/pipeline/${dealId}`);
  return {};
}

export async function draftEmailAction(dealId: string, instruction: string): Promise<{ jobId: string }> {
  const { api } = await requireWorkspace();
  const { data, error } = await api.POST("/api/deals/{dealId}/draft-email", {
    params: { path: { dealId } },
    body: { instruction: instruction.trim() === "" ? null : instruction },
  });
  if (error || !data) throw new Error("Deal not found in this workspace");
  return { jobId: data.jobId };
}

export type DraftJobResult =
  | { status: "pending" | "processing"; result: null }
  | { status: "succeeded"; result: { subject: string; body: string } }
  | { status: "failed"; error: string }
  | { status: "not_found" };

export async function getDraftJobStatusAction(jobId: string): Promise<DraftJobResult> {
  const { api } = await requireWorkspace();
  const { data, response } = await api.GET("/api/jobs/{jobId}", { params: { path: { jobId } } });
  if (response.status === 404 || !data) return { status: "not_found" };

  if (data.status === "succeeded") {
    try {
      const result = JSON.parse(data.result ?? "{}") as { subject?: string; body?: string };
      if (typeof result.subject === "string" && typeof result.body === "string") {
        return { status: "succeeded", result: { subject: result.subject, body: result.body } };
      }
    } catch {
      // fall through to failed below
    }
    return { status: "failed", error: "Draft came back in an unexpected shape" };
  }
  if (data.status === "failed") {
    return { status: "failed", error: data.lastError ?? "Email drafting is temporarily unavailable" };
  }
  return { status: data.status === "processing" ? "processing" : "pending", result: null };
}

export type SaveListingState = { error?: string };

// Real-estate optional module (workspace-customization skill §4) — only
// reachable when this workspace has "listings" in EnabledModules; the API
// itself enforces that (403), this is just where the form posts to.
export async function saveListingAction(
  _prevState: SaveListingState,
  formData: FormData,
): Promise<SaveListingState> {
  const { api } = await requireWorkspace();

  const dealId = formData.get("dealId");
  if (typeof dealId !== "string") return { error: "Missing deal" };

  const listingAgentName = (formData.get("listingAgentName") as string | null)?.trim() || null;
  const listingUrl = (formData.get("listingUrl") as string | null)?.trim() || null;
  const openHouseAtRaw = (formData.get("openHouseAt") as string | null)?.trim() || null;
  const commissionPercentRaw = (formData.get("commissionPercent") as string | null)?.trim() || null;

  const commissionPercent = commissionPercentRaw === null ? null : Number(commissionPercentRaw);
  if (commissionPercent !== null && Number.isNaN(commissionPercent)) {
    return { error: "Commission percent must be a number" };
  }

  const { error } = await api.PUT("/api/deals/{dealId}/listing", {
    params: { path: { dealId } },
    body: {
      listingAgentName,
      listingUrl,
      openHouseAt: openHouseAtRaw ? new Date(openHouseAtRaw).toISOString() : null,
      commissionPercent,
    },
  });
  if (error) return { error: "Could not save the listing" };

  revalidatePath(`/pipeline/${dealId}`);
  return {};
}

export async function summarizeDealAction(dealId: string): Promise<{ jobId: string }> {
  const { api } = await requireWorkspace();
  const { data, error } = await api.POST("/api/deals/{dealId}/summarize", {
    params: { path: { dealId } },
  });
  if (error || !data) throw new Error("Deal not found in this workspace");
  return { jobId: data.jobId };
}

export type SummarizeJobResult =
  | { status: "pending" | "processing" }
  | { status: "succeeded" }
  | { status: "failed"; error: string }
  | { status: "not_found" };

export async function getSummarizeJobStatusAction(jobId: string): Promise<SummarizeJobResult> {
  const { api } = await requireWorkspace();
  const { data, response } = await api.GET("/api/jobs/{jobId}", { params: { path: { jobId } } });
  if (response.status === 404 || !data) return { status: "not_found" };

  if (data.status === "succeeded") return { status: "succeeded" };
  if (data.status === "failed") return { status: "failed", error: data.lastError ?? "Summarization is temporarily unavailable" };
  return { status: data.status === "processing" ? "processing" : "pending" };
}

export async function suggestNextActionsAction(dealId: string): Promise<{ jobId: string }> {
  const { api } = await requireWorkspace();
  const { data, error } = await api.POST("/api/deals/{dealId}/next-best-action", {
    params: { path: { dealId } },
  });
  if (error || !data) throw new Error("Deal not found in this workspace");
  return { jobId: data.jobId };
}

export type NextBestActionSuggestion = { action: string; reasoning: string; confidence: string };

export type NextBestActionJobResult =
  | { status: "pending" | "processing"; suggestions: null }
  | { status: "succeeded"; suggestions: NextBestActionSuggestion[] }
  | { status: "failed"; error: string }
  | { status: "not_found" };

export async function getNextBestActionJobStatusAction(jobId: string): Promise<NextBestActionJobResult> {
  const { api } = await requireWorkspace();
  const { data, response } = await api.GET("/api/jobs/{jobId}", { params: { path: { jobId } } });
  if (response.status === 404 || !data) return { status: "not_found" };

  if (data.status === "succeeded") {
    try {
      const result = JSON.parse(data.result ?? "{}") as { suggestions?: NextBestActionSuggestion[] };
      if (Array.isArray(result.suggestions)) {
        return { status: "succeeded", suggestions: result.suggestions };
      }
    } catch {
      // fall through to failed below
    }
    return { status: "failed", error: "Suggestions came back in an unexpected shape" };
  }
  if (data.status === "failed") {
    return { status: "failed", error: data.lastError ?? "Next-best-action is temporarily unavailable" };
  }
  return { status: data.status === "processing" ? "processing" : "pending", suggestions: null };
}
