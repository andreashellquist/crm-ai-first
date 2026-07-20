"use client";

import { useActionState } from "react";
import { updateProfileAction } from "./actions";
import { Button } from "@/components/ui/button";

export function MyProfileForm({ name, timezone }: { name: string | null; timezone: string }) {
  const [error, formAction, pending] = useActionState(updateProfileAction, undefined);

  return (
    <form action={formAction} className="space-y-2">
      <div className="flex flex-wrap items-end gap-2">
        <label className="text-sm">
          <span className="block text-neutral-500">Name</span>
          <input
            type="text"
            name="name"
            defaultValue={name ?? ""}
            className="mt-1 w-48 rounded-md border border-neutral-300 px-2 py-1"
          />
        </label>
        <label className="text-sm">
          <span className="block text-neutral-500">Timezone (IANA)</span>
          <input
            type="text"
            name="timezone"
            defaultValue={timezone}
            placeholder="America/New_York"
            className="mt-1 w-48 rounded-md border border-neutral-300 px-2 py-1"
          />
        </label>
        <Button type="submit" disabled={pending}>
          {pending ? "Saving…" : "Save profile"}
        </Button>
      </div>
      {error ? <p className="text-sm text-red-600">{error}</p> : null}
    </form>
  );
}
