"use server";

import { requireWorkspace } from "@/lib/workspace";

export async function refreshReportsAction(): Promise<{ jobId: string }> {
  const { api } = await requireWorkspace();
  const { data, error } = await api.POST("/api/reports/refresh");
  if (error || !data) throw new Error("Could not refresh reports");
  return { jobId: data.jobId };
}

export type RefreshJobResult = { status: "pending" | "processing" } | { status: "succeeded" } | { status: "failed"; error: string } | { status: "not_found" };

export async function getRefreshJobStatusAction(jobId: string): Promise<RefreshJobResult> {
  const { api } = await requireWorkspace();
  const { data, response } = await api.GET("/api/jobs/{jobId}", { params: { path: { jobId } } });
  if (response.status === 404 || !data) return { status: "not_found" };

  if (data.status === "succeeded") return { status: "succeeded" };
  if (data.status === "failed") return { status: "failed", error: data.lastError ?? "Refresh is temporarily unavailable" };
  return { status: data.status === "processing" ? "processing" : "pending" };
}
