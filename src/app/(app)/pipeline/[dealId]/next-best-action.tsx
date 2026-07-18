"use client";

import { useEffect, useRef, useState } from "react";
import {
  suggestNextActionsAction,
  getNextBestActionJobStatusAction,
  type NextBestActionSuggestion,
} from "./actions";
import { Button } from "@/components/ui/button";

const POLL_INTERVAL_MS = 1200;
const MAX_POLLS = 25; // ~30s before giving up and telling the user to check back

const CONFIDENCE_STYLES: Record<string, string> = {
  high: "bg-indigo-200 text-indigo-800",
  medium: "bg-indigo-100 text-indigo-700",
  low: "bg-indigo-50 text-indigo-600",
};

// Read-only suggestions — see ai-features-architect: "next-best-action
// suggestions are proposals a rep reviews, never an action the app takes on
// its own." There is no "execute" button here on purpose; acting on a
// suggestion means the rep logging an activity, drafting an email, etc.
// through the app's normal, explicit flows.
export function NextBestAction({ dealId }: { dealId: string }) {
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [suggestions, setSuggestions] = useState<NextBestActionSuggestion[] | null>(null);
  const stopPolling = useRef(false);

  useEffect(() => () => {
    stopPolling.current = true;
  }, []);

  async function handleSuggest() {
    setLoading(true);
    setError(null);
    stopPolling.current = false;

    let jobId: string;
    try {
      ({ jobId } = await suggestNextActionsAction(dealId));
    } catch {
      setLoading(false);
      setError("Could not start — try again.");
      return;
    }

    for (let attempt = 0; attempt < MAX_POLLS && !stopPolling.current; attempt++) {
      await new Promise((resolve) => setTimeout(resolve, POLL_INTERVAL_MS));
      const result = await getNextBestActionJobStatusAction(jobId);

      if (result.status === "succeeded") {
        setLoading(false);
        setSuggestions(result.suggestions);
        return;
      }
      if (result.status === "failed") {
        setLoading(false);
        setError(result.error);
        return;
      }
      // "pending" / "processing" / "not_found" (job not yet visible) — keep polling
    }

    if (!stopPolling.current) {
      setLoading(false);
      setError("Still working — check back in a moment.");
    }
  }

  return (
    <div className="space-y-3">
      <div className="flex items-center justify-between">
        <h2 className="text-sm font-semibold text-neutral-700">Next best action</h2>
        <Button type="button" variant="outline" size="sm" disabled={loading} onClick={handleSuggest}>
          {loading ? "Thinking…" : suggestions ? "Suggest again" : "Suggest with AI"}
        </Button>
      </div>

      {error ? <p className="text-sm text-red-600">{error}</p> : null}

      {/* Suggestions only — no execute button. Acting on one means using the
          app's normal flows (log an activity, draft an email, ...). */}
      {suggestions ? (
        <ul className="space-y-2">
          {suggestions.map((suggestion, index) => (
            <li key={index} className="rounded border border-indigo-200 bg-indigo-50 p-3 text-sm">
              <div className="flex items-start justify-between gap-2">
                <div className="flex items-center gap-1 font-medium text-indigo-900">
                  <span aria-hidden>✦</span> {suggestion.action}
                </div>
                <span
                  className={`shrink-0 rounded px-1.5 py-0.5 text-xs font-medium ${CONFIDENCE_STYLES[suggestion.confidence] ?? CONFIDENCE_STYLES.low}`}
                >
                  {suggestion.confidence}
                </span>
              </div>
              <p className="mt-1 text-indigo-800">{suggestion.reasoning}</p>
            </li>
          ))}
        </ul>
      ) : null}
    </div>
  );
}
