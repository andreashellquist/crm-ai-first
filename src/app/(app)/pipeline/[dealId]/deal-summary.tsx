"use client";

import { useEffect, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import { summarizeDealAction, getSummarizeJobStatusAction } from "./actions";
import { Button } from "@/components/ui/button";

const POLL_INTERVAL_MS = 1200;
const MAX_POLLS = 25; // ~30s before giving up and telling the user to check back

// Summarize-on-read with an incremental cache — see ai-features-architect
// and SummarizationService. The summary itself lives on the Deal (server
// component data), not client state — a successful job just triggers
// router.refresh() to pick up the new Deal.aiSummary, same pattern as
// DealCard's "Re-score with AI".
export function DealSummary({
  dealId,
  summary,
  summarizedAt,
  activitiesSinceSummary,
}: {
  dealId: string;
  summary: string | null;
  summarizedAt: string | null;
  activitiesSinceSummary: number;
}) {
  const router = useRouter();
  const [summarizing, setSummarizing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const stopPolling = useRef(false);

  useEffect(() => () => {
    stopPolling.current = true;
  }, []);

  async function handleSummarize() {
    setSummarizing(true);
    setError(null);
    stopPolling.current = false;

    let jobId: string;
    try {
      ({ jobId } = await summarizeDealAction(dealId));
    } catch {
      setSummarizing(false);
      setError("Could not start summarizing — try again.");
      return;
    }

    for (let attempt = 0; attempt < MAX_POLLS && !stopPolling.current; attempt++) {
      await new Promise((resolve) => setTimeout(resolve, POLL_INTERVAL_MS));
      const result = await getSummarizeJobStatusAction(jobId);

      if (result.status === "succeeded") {
        setSummarizing(false);
        router.refresh();
        return;
      }
      if (result.status === "failed") {
        setSummarizing(false);
        setError(result.error);
        return;
      }
    }

    if (!stopPolling.current) {
      setSummarizing(false);
      setError("Still working — check back in a moment.");
    }
  }

  const isStale = summary != null && activitiesSinceSummary > 0;

  return (
    <div className="space-y-2">
      <div className="flex items-center justify-between">
        <h2 className="text-sm font-semibold text-neutral-700">Summary</h2>
        <Button type="button" variant="outline" size="sm" disabled={summarizing} onClick={handleSummarize}>
          {summarizing ? "Summarizing…" : summary ? "Re-summarize" : "Summarize with AI"}
        </Button>
      </div>

      {error ? <p className="text-sm text-red-600">{error}</p> : null}

      {summary ? (
        <div className="rounded border border-indigo-200 bg-indigo-50 p-3 text-sm">
          <div className="flex items-center gap-1 text-xs font-medium text-indigo-700">
            <span aria-hidden>✦</span> AI summary
            {summarizedAt ? <span className="font-normal text-indigo-500"> · {new Date(summarizedAt).toLocaleString()}</span> : null}
          </div>
          <p className="mt-1 text-indigo-900">{summary}</p>
          {isStale ? (
            <p className="mt-2 text-xs text-indigo-600">
              {activitiesSinceSummary} new {activitiesSinceSummary === 1 ? "activity" : "activities"} since this summary — re-summarize to catch up.
            </p>
          ) : null}
        </div>
      ) : (
        <p className="text-sm text-neutral-400">No summary yet.</p>
      )}
    </div>
  );
}
