"use client";

import { useEffect, useRef, useState } from "react";
import { draftEmailAction, getDraftJobStatusAction } from "./actions";
import { Button } from "@/components/ui/button";

const POLL_INTERVAL_MS = 1200;
const MAX_POLLS = 25; // ~30s before giving up and telling the user to check back

// Draft-and-review, never auto-sent (ai-features-architect) — the result
// lands in editable textareas, not a "send" button; there's no send
// capability in this app yet at all.
export function DraftEmail({ dealId }: { dealId: string }) {
  const [instruction, setInstruction] = useState("");
  const [drafting, setDrafting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [draft, setDraft] = useState<{ subject: string; body: string } | null>(null);
  const stopPolling = useRef(false);

  useEffect(() => () => {
    stopPolling.current = true;
  }, []);

  async function handleDraft() {
    setDrafting(true);
    setError(null);
    setDraft(null);
    stopPolling.current = false;

    let jobId: string;
    try {
      ({ jobId } = await draftEmailAction(dealId, instruction));
    } catch {
      setDrafting(false);
      setError("Could not start drafting — try again.");
      return;
    }

    for (let attempt = 0; attempt < MAX_POLLS && !stopPolling.current; attempt++) {
      await new Promise((resolve) => setTimeout(resolve, POLL_INTERVAL_MS));
      const result = await getDraftJobStatusAction(jobId);

      if (result.status === "succeeded") {
        setDrafting(false);
        setDraft(result.result);
        return;
      }
      if (result.status === "failed") {
        setDrafting(false);
        setError(result.error);
        return;
      }
      // "pending" / "processing" / "not_found" (job not yet visible) — keep polling
    }

    if (!stopPolling.current) {
      setDrafting(false);
      setError("Still working — check back in a moment.");
    }
  }

  return (
    <div className="space-y-3">
      <h2 className="text-sm font-semibold text-neutral-700">Draft a follow-up email</h2>
      <label className="block text-xs text-neutral-500">
        Instructions (optional)
        <textarea
          className="mt-1 w-full rounded border border-neutral-300 px-2 py-1 text-sm"
          rows={2}
          placeholder="e.g. mention the updated pricing, keep it short"
          value={instruction}
          onChange={(event) => setInstruction(event.target.value)}
          disabled={drafting}
        />
      </label>

      <Button type="button" variant="outline" size="sm" disabled={drafting} onClick={handleDraft}>
        {drafting ? "Drafting…" : draft ? "Draft another" : "Draft with AI"}
      </Button>

      {error ? <p className="text-sm text-red-600">{error}</p> : null}

      {/* AI-generated content gets a visually distinct treatment — same
          convention as the deal-scoring card (frontend-engineer /
          ai-features-architect). Editable: this is a starting point, not a
          finished, sendable email. */}
      {draft ? (
        <div className="space-y-2 rounded border border-indigo-200 bg-indigo-50 p-3">
          <div className="flex items-center gap-1 text-xs font-medium text-indigo-700">
            <span aria-hidden>✦</span> AI draft — review and edit before using
          </div>
          <label className="block text-xs text-neutral-600">
            Subject
            <input
              className="mt-1 w-full rounded border border-neutral-300 bg-white px-2 py-1 text-sm"
              value={draft.subject}
              onChange={(event) => setDraft({ ...draft, subject: event.target.value })}
            />
          </label>
          <label className="block text-xs text-neutral-600">
            Body
            <textarea
              className="mt-1 w-full rounded border border-neutral-300 bg-white px-2 py-1 text-sm"
              rows={8}
              value={draft.body}
              onChange={(event) => setDraft({ ...draft, body: event.target.value })}
            />
          </label>
        </div>
      ) : null}
    </div>
  );
}
