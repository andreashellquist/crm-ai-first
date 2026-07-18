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
