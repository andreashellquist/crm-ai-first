"use server";

import { requireWorkspace } from "@/lib/workspace";

export async function updateModulesAction(_prevState: string | undefined, formData: FormData) {
  const { api } = await requireWorkspace();
  const enabledModules = formData.getAll("modules").map(String);

  const { error } = await api.PUT("/api/workspace/settings", {
    body: { terminology: null, enabledModules },
  });

  if (error) return "You need to be an owner or admin to change workspace settings.";
  return undefined;
}
