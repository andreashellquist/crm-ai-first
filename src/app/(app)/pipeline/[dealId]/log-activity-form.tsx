"use client";

import { useActionState, useEffect, useRef } from "react";
import { logActivityAction, type LogActivityState } from "./actions";
import { Button } from "@/components/ui/button";

const initialState: LogActivityState = {};

const ACTIVITY_TYPES = [
  { value: "call", label: "Call" },
  { value: "email", label: "Email" },
  { value: "meeting", label: "Meeting" },
  { value: "note", label: "Note" },
];

export function LogActivityForm({ dealId }: { dealId: string }) {
  const [state, formAction, pending] = useActionState(logActivityAction, initialState);
  const formRef = useRef<HTMLFormElement>(null);

  useEffect(() => {
    if (!pending && !state.error) formRef.current?.reset();
  }, [pending, state]);

  return (
    <form ref={formRef} action={formAction} className="space-y-2 rounded-lg border border-neutral-200 p-3">
      <input type="hidden" name="dealId" value={dealId} />
      <div className="flex items-center gap-2">
        <label htmlFor="type" className="text-xs font-medium text-neutral-600">
          Log
        </label>
        <select
          id="type"
          name="type"
          defaultValue="note"
          className="rounded border border-neutral-300 bg-white px-2 py-1 text-sm"
        >
          {ACTIVITY_TYPES.map((t) => (
            <option key={t.value} value={t.value}>
              {t.label}
            </option>
          ))}
        </select>
      </div>
      <textarea
        name="body"
        rows={3}
        placeholder="What happened?"
        className="w-full rounded border border-neutral-300 px-2 py-1 text-sm"
        required
      />
      {state.error ? <p className="text-sm text-red-600">{state.error}</p> : null}
      <Button type="submit" size="sm" disabled={pending}>
        {pending ? "Logging…" : "Log activity"}
      </Button>
    </form>
  );
}
