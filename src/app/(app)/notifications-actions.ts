"use server";

import { revalidatePath } from "next/cache";
import { requireWorkspace } from "@/lib/workspace";

export type NotificationItem = {
  id: string;
  type: string;
  entityType: string | null;
  entityId: string | null;
  readAt: string | null;
  createdAt: string;
};

export async function getUnreadCountAction(): Promise<number> {
  const { api } = await requireWorkspace();
  const { data } = await api.GET("/api/notifications/unread-count");
  // .NET's OpenAPI generator renders `int` as `number | string` regardless
  // of nullability — coerce at the boundary (established convention, see
  // e.g. pipeline/[dealId]/page.tsx's activitiesSinceSummary).
  return data ? Number(data.count) : 0;
}

export async function listNotificationsAction(): Promise<NotificationItem[]> {
  const { api } = await requireWorkspace();
  const { data } = await api.GET("/api/notifications");
  return data ?? [];
}

export async function markNotificationReadAction(id: string): Promise<void> {
  const { api } = await requireWorkspace();
  await api.POST("/api/notifications/{id}/read", { params: { path: { id } } });
  revalidatePath("/", "layout");
}

export async function markAllNotificationsReadAction(): Promise<void> {
  const { api } = await requireWorkspace();
  await api.POST("/api/notifications/read-all");
  revalidatePath("/", "layout");
}
