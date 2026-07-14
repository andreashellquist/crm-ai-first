"use client";

import { useTransition } from "react";
import { moveDealStageAction } from "./actions";

type Stage = { id: string; name: string };

export function DealCard({
  dealId,
  title,
  amountLabel,
  stages,
  currentStageId,
}: {
  dealId: string;
  title: string;
  amountLabel: string | null;
  stages: Stage[];
  currentStageId: string;
}) {
  const [pending, startTransition] = useTransition();

  return (
    <div className="space-y-2 rounded-md border border-neutral-200 bg-white p-3 shadow-sm">
      <div className="text-sm font-medium">{title}</div>
      {amountLabel ? <div className="text-xs text-neutral-500">{amountLabel}</div> : null}
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
    </div>
  );
}
