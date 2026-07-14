"use server";

import { z } from "zod";
import { revalidatePath } from "next/cache";
import { db } from "@/lib/db";
import { requireWorkspace } from "@/lib/workspace";

const logActivitySchema = z.object({
  dealId: z.string(),
  type: z.enum(["call", "email", "meeting", "note"]),
  body: z.string().min(1, "Enter some notes"),
});

export type LogActivityState = { error?: string };

export async function logActivityAction(
  _prevState: LogActivityState,
  formData: FormData,
): Promise<LogActivityState> {
  const { workspaceId } = await requireWorkspace();

  const parsed = logActivitySchema.safeParse({
    dealId: formData.get("dealId"),
    type: formData.get("type"),
    body: formData.get("body"),
  });
  if (!parsed.success) {
    return { error: parsed.error.issues[0]?.message ?? "Invalid input" };
  }

  // Re-check the deal belongs to this workspace before attaching an Activity
  // to it — same discipline as every other mutation (database-schema-expert).
  const deal = await db.deal.findFirst({
    where: { id: parsed.data.dealId, workspaceId },
    select: { id: true, companyId: true },
  });
  if (!deal) return { error: "Deal not found" };

  await db.activity.create({
    data: {
      workspaceId,
      dealId: deal.id,
      companyId: deal.companyId,
      type: parsed.data.type,
      body: parsed.data.body,
    },
  });

  revalidatePath(`/pipeline/${deal.id}`);
  return {};
}
