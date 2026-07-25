"use server";

import { requireWorkspace } from "@/lib/workspace";

export async function askQuestionAction(question: string): Promise<{ jobId: string }> {
  const { api } = await requireWorkspace();
  const { data, error } = await api.POST("/api/ask", {
    body: { question, dealId: null, contactId: null, companyId: null },
  });
  if (error || !data) throw new Error("Could not ask that question — try again.");
  return { jobId: data.jobId };
}

export type AskJobResult =
  | { status: "pending" | "processing"; result: null }
  | { status: "succeeded"; result: { answer: string; citations: Citation[] } }
  | { status: "failed"; error: string }
  | { status: "not_found" };

export type Citation = {
  activityId: string;
  type: string;
  snippet: string;
  createdAt: string;
  dealId: string | null;
  contactId: string | null;
  companyId: string | null;
};

export async function getAskJobStatusAction(jobId: string): Promise<AskJobResult> {
  const { api } = await requireWorkspace();
  const { data, response } = await api.GET("/api/jobs/{jobId}", { params: { path: { jobId } } });
  if (response.status === 404 || !data) return { status: "not_found" };

  if (data.status === "succeeded") {
    try {
      const result = JSON.parse(data.result ?? "{}") as { answer?: string; citations?: Citation[] };
      if (typeof result.answer === "string" && Array.isArray(result.citations)) {
        return { status: "succeeded", result: { answer: result.answer, citations: result.citations } };
      }
    } catch {
      // fall through to failed below
    }
    return { status: "failed", error: "Answer came back in an unexpected shape" };
  }
  if (data.status === "failed") return { status: "failed", error: data.lastError ?? "Ask AI is temporarily unavailable" };
  return { status: data.status === "processing" ? "processing" : "pending", result: null };
}
