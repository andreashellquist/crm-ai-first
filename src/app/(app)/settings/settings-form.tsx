"use client";

import { useActionState } from "react";
import { updateModulesAction } from "./actions";
import { Button } from "@/components/ui/button";

export function SettingsForm({
  modules,
  enabledModules,
}: {
  modules: { id: string; name: string; description: string }[];
  enabledModules: string[];
}) {
  const [error, formAction, pending] = useActionState(updateModulesAction, undefined);

  return (
    <form action={formAction} className="space-y-3">
      <div className="space-y-2">
        {modules.map((module) => (
          <label key={module.id} className="flex items-start gap-2 rounded-md border border-neutral-200 p-3 text-sm">
            <input
              type="checkbox"
              name="modules"
              value={module.id}
              defaultChecked={enabledModules.includes(module.id)}
              className="mt-0.5"
            />
            <span>
              <span className="font-medium">{module.name}</span>
              <span className="block text-xs text-neutral-500">{module.description}</span>
            </span>
          </label>
        ))}
      </div>

      {error ? <p className="text-sm text-red-600">{error}</p> : null}

      <Button type="submit" disabled={pending}>
        {pending ? "Saving…" : "Save modules"}
      </Button>
    </form>
  );
}
