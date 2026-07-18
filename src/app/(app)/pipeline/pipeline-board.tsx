"use client";

import { useState } from "react";
import { formatAmount, sumCents } from "@/lib/money";
import { moveDealStageAction } from "./actions";
import { DealCard } from "./deal-card";
import type { components } from "@/lib/api/schema";

type PipelineBoard = components["schemas"]["PipelineBoardDto"];
type Stage = components["schemas"]["StageDto"];

// Drag-and-drop with optimistic update, revert-on-failure — see the
// pipeline-kanban-board skill. handleMove is the single function both the
// drop handler and DealCard's "Move to stage" <select> fallback call, so
// drag-and-drop is a UI affordance layered on the same mutation, never a
// second code path that can drift from it.
export function PipelineBoard({ pipeline }: { pipeline: PipelineBoard }) {
  // Resync from fresh server data whenever a new `pipeline` prop arrives —
  // e.g. DealCard's "Re-score with AI" flow calls router.refresh() after a
  // score completes. Local optimistic state only needs to hold *between* a
  // drop and the server confirming it; once fresher data exists, it's the
  // source of truth (pipeline-kanban-board skill's "reconcile with the
  // server response" step). Adjusted during render, not in an effect, per
  // React's "resetting state when a prop changes" pattern — avoids an extra
  // commit + the react-hooks/set-state-in-effect lint rule.
  const [prevPipeline, setPrevPipeline] = useState(pipeline);
  const [stages, setStages] = useState<Stage[]>(pipeline.stages);
  if (pipeline !== prevPipeline) {
    setPrevPipeline(pipeline);
    setStages(pipeline.stages);
  }

  const [draggingDealId, setDraggingDealId] = useState<string | null>(null);
  const [dragOverStageId, setDragOverStageId] = useState<string | null>(null);
  const [moveError, setMoveError] = useState<string | null>(null);

  async function handleMove(dealId: string, targetStageId: string) {
    setMoveError(null);
    const sourceStage = stages.find((s) => s.deals.some((d) => d.id === dealId));
    if (!sourceStage || sourceStage.id === targetStageId) return;

    const deal = sourceStage.deals.find((d) => d.id === dealId)!;
    const previousStages = stages;

    // 1. Optimistic move — the card jumps columns before the server responds.
    setStages(
      stages.map((stage) => {
        if (stage.id === sourceStage.id) return { ...stage, deals: stage.deals.filter((d) => d.id !== dealId) };
        if (stage.id === targetStageId) return { ...stage, deals: [deal, ...stage.deals] };
        return stage;
      }),
    );

    // 2. Fire the mutation.
    try {
      await moveDealStageAction({ dealId, stageId: targetStageId });
    } catch {
      // 3. Revert on failure — never leave the board showing a state the
      // server didn't accept (pipeline-kanban-board skill).
      setStages(previousStages);
      setMoveError("Couldn't move that deal — try again.");
    }
  }

  const allStages = stages.map((stage) => ({ id: stage.id, name: stage.name }));

  return (
    <div className="space-y-2">
      {moveError ? <p className="text-sm text-red-600">{moveError}</p> : null}
      <div className="flex gap-4 overflow-x-auto pb-4">
        {stages.map((stage) => {
          const stageValueCents = sumCents(stage.deals.map((deal) => deal.amountCents));
          const isDragOver = dragOverStageId === stage.id;
          return (
            <div
              key={stage.id}
              className={`w-64 flex-none rounded-lg p-3 transition-colors ${
                isDragOver ? "bg-indigo-50 ring-2 ring-indigo-300" : "bg-neutral-50"
              }`}
              onDragOver={(event) => {
                if (!draggingDealId) return;
                event.preventDefault(); // required to allow a drop
                setDragOverStageId(stage.id);
              }}
              onDragLeave={() => setDragOverStageId((current) => (current === stage.id ? null : current))}
              onDrop={(event) => {
                event.preventDefault();
                setDragOverStageId(null);
                const dealId = event.dataTransfer.getData("text/plain") || draggingDealId;
                if (dealId) void handleMove(dealId, stage.id);
                setDraggingDealId(null);
              }}
            >
              <div className="mb-3 flex items-baseline justify-between">
                <h2 className="text-sm font-semibold">{stage.name}</h2>
                <span className="text-xs text-neutral-500">{stage.deals.length}</span>
              </div>
              <div className="mb-3 text-xs text-neutral-500">{formatAmount(stageValueCents, "USD")}</div>
              <div className="space-y-2">
                {stage.deals.map((deal) => (
                  <div
                    key={deal.id}
                    draggable
                    onDragStart={(event) => {
                      event.dataTransfer.setData("text/plain", deal.id);
                      event.dataTransfer.effectAllowed = "move";
                      setDraggingDealId(deal.id);
                    }}
                    onDragEnd={() => {
                      setDraggingDealId(null);
                      setDragOverStageId(null);
                    }}
                    className={draggingDealId === deal.id ? "cursor-grabbing opacity-50" : "cursor-grab"}
                  >
                    <DealCard
                      dealId={deal.id}
                      title={deal.title}
                      amountLabel={formatAmount(deal.amountCents, deal.currency)}
                      stages={allStages}
                      currentStageId={stage.id}
                      aiScore={deal.aiScore == null ? null : Number(deal.aiScore)}
                      aiScoreRationale={deal.aiScoreRationale ?? null}
                      onMove={(stageId) => void handleMove(deal.id, stageId)}
                    />
                  </div>
                ))}
                {stage.deals.length === 0 ? <p className="text-xs text-neutral-400">No deals</p> : null}
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}
