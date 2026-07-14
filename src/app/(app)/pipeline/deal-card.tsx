"use client";

import { useEffect, useRef, useState, useTransition } from "react";
import { useRouter } from "next/navigation";
import Link from "next/link";
import { moveDealStageAction, scoreDealAction, getJobStatusAction } from "./actions";
import { Button } from "@/components/ui/button";

type Stage = { id: string; name: string };

const POLL_INTERVAL_MS = 1200;
const MAX_POLLS = 25; // ~30s before giving up and telling the user to check back

export function DealCard({
  dealId,
  title,
  amountLabel,
  stages,
  currentStageId,
  aiScore,
  aiScoreRationale,
}: {
  dealId: string;
  title: string;
  amountLabel: string | null;
  stages: Stage[];
  currentStageId: string;
  aiScore: number | null;
  aiScoreRationale: string | null;
}) {
  const router = useRouter();
  const [pending, startTransition] = useTransition();
  const [scoring, setScoring] = useState(false);
  const [scoreError, setScoreError] = useState<string | null>(null);
  const stopPolling = useRef(false);

  useEffect(() => () => {
    stopPolling.current = true;
  }, []);

  async function handleScore() {
    setScoring(true);
    setScoreError(null);
    stopPolling.current = false;

    let jobId: string;
    try {
      ({ jobId } = await scoreDealAction(dealId));
    } catch {
      setScoring(false);
      setScoreError("Could not queue scoring — try again.");
      return;
    }

    for (let attempt = 0; attempt < MAX_POLLS && !stopPolling.current; attempt++) {
      await new Promise((resolve) => setTimeout(resolve, POLL_INTERVAL_MS));
      const result = await getJobStatusAction(jobId);

      if (result.status === "succeeded") {
        setScoring(false);
        router.refresh(); // re-fetches the server component tree with the new score
        return;
      }
      if (result.status === "failed") {
        setScoring(false);
        setScoreError(result.lastError ?? "Deal scoring is temporarily unavailable");
        return;
      }
      // "pending" / "processing" / "not_found" (job not yet visible) — keep polling
    }

    if (!stopPolling.current) {
      setScoring(false);
      setScoreError("Still working — check back in a moment.");
    }
  }

  return (
    <div className="space-y-2 rounded-md border border-neutral-200 bg-white p-3 shadow-sm">
      <Link href={`/pipeline/${dealId}`} className="block text-sm font-medium hover:underline">
        {title}
      </Link>
      {amountLabel ? <div className="text-xs text-neutral-500">{amountLabel}</div> : null}

      {/* AI-generated content gets a visually distinct treatment so it's never
          confused with human-entered data (frontend-engineer / ai-features-architect). */}
      {aiScore != null ? (
        <div className="rounded border border-indigo-200 bg-indigo-50 p-2 text-xs">
          <div className="flex items-center gap-1 font-medium text-indigo-700">
            <span aria-hidden>✦</span> AI score: {aiScore}/100
          </div>
          {aiScoreRationale ? <p className="mt-1 text-indigo-900">{aiScoreRationale}</p> : null}
        </div>
      ) : null}
      {scoreError ? <p className="text-xs text-red-600">{scoreError}</p> : null}

      {/* Explicit "move to stage" control rather than drag-and-drop for this
          walking skeleton — keeps stage changes keyboard/screen-reader
          operable by default (pipeline-kanban-board skill's accessibility
          fallback requirement). Drag-and-drop can be layered on top later,
          calling this same action. */}
      <label className="block text-xs text-neutral-500">
        Stage
        <select
          className="mt-1 w-full rounded border border-neutral-300 bg-white px-2 py-1 text-xs disabled:opacity-50"
          value={currentStageId}
          disabled={pending}
          onChange={(event) => {
            const stageId = event.target.value;
            startTransition(() => {
              void moveDealStageAction({ dealId, stageId });
            });
          }}
        >
          {stages.map((stage) => (
            <option key={stage.id} value={stage.id}>
              {stage.name}
            </option>
          ))}
        </select>
      </label>

      <Button
        type="button"
        variant="outline"
        size="sm"
        className="w-full"
        disabled={scoring}
        onClick={handleScore}
      >
        {scoring ? "Scoring…" : aiScore != null ? "Re-score with AI" : "Score with AI"}
      </Button>
    </div>
  );
}
