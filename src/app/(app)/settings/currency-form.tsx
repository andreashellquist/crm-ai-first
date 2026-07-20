"use client";

import { useActionState } from "react";
import { updateCurrencyAction } from "./actions";
import { Button } from "@/components/ui/button";

export function CurrencyForm({ defaultCurrency }: { defaultCurrency: string }) {
  const [error, formAction, pending] = useActionState(updateCurrencyAction, undefined);

  return (
    <form action={formAction} className="space-y-2">
      <div className="flex items-end gap-2">
        <label className="text-sm">
          <span className="block text-neutral-500">Default currency</span>
          <input
            type="text"
            name="defaultCurrency"
            defaultValue={defaultCurrency}
            maxLength={3}
            className="mt-1 w-20 rounded-md border border-neutral-300 px-2 py-1 uppercase"
          />
        </label>
        <Button type="submit" disabled={pending}>
          {pending ? "Saving…" : "Save currency"}
        </Button>
      </div>
      {error ? <p className="text-sm text-red-600">{error}</p> : null}
    </form>
  );
}
