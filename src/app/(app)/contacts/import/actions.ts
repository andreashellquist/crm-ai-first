"use server";

import { revalidatePath } from "next/cache";
import { requireWorkspace } from "@/lib/workspace";

export type ImportPreview = { headers: string[]; previewRows: Record<string, string>[]; totalRows: number };

export async function previewImportAction(csvContent: string): Promise<ImportPreview> {
  const { api } = await requireWorkspace();
  const { data, error } = await api.POST("/api/contacts/import/preview", { body: { csvContent } });
  if (error || !data) throw new Error(typeof error === "string" ? error : "Could not read that CSV file");
  // .NET's OpenAPI generator renders `int` as `number | string` regardless of
  // nullability — coerce at the boundary (same convention as elsewhere, e.g.
  // pipeline/[dealId]/page.tsx's activitiesSinceSummary).
  return { ...data, totalRows: Number(data.totalRows) };
}

export async function startImportAction(csvContent: string, columnMapping: Record<string, string>): Promise<{ jobId: string }> {
  const { api } = await requireWorkspace();
  const { data, error } = await api.POST("/api/contacts/import", { body: { csvContent, columnMapping } });
  if (error || !data) throw new Error("Could not start the import");
  return { jobId: data.jobId };
}

export type ImportRowError = { row: number; message: string };
export type ImportSummary = { totalRows: number; created: number; updated: number; skipped: number; errors: ImportRowError[] };

export type ImportJobResult =
  | { status: "pending" | "processing"; summary: null }
  | { status: "succeeded"; summary: ImportSummary }
  | { status: "failed"; error: string }
  | { status: "not_found" };

export async function getImportJobStatusAction(jobId: string): Promise<ImportJobResult> {
  const { api } = await requireWorkspace();
  const { data, response } = await api.GET("/api/jobs/{jobId}", { params: { path: { jobId } } });
  if (response.status === 404 || !data) return { status: "not_found" };

  if (data.status === "succeeded") {
    try {
      const summary = JSON.parse(data.result ?? "{}") as Partial<ImportSummary>;
      if (typeof summary.created === "number" && typeof summary.updated === "number") {
        revalidatePath("/contacts");
        return {
          status: "succeeded",
          summary: {
            totalRows: Number(summary.totalRows ?? 0),
            created: summary.created,
            updated: summary.updated,
            skipped: Number(summary.skipped ?? 0),
            errors: summary.errors ?? [],
          },
        };
      }
    } catch {
      // fall through to failed below
    }
    return { status: "failed", error: "Import result came back in an unexpected shape" };
  }
  if (data.status === "failed") {
    return { status: "failed", error: data.lastError ?? "Import is temporarily unavailable" };
  }
  return { status: data.status === "processing" ? "processing" : "pending", summary: null };
}
