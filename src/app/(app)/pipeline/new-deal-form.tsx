"use client";

import { useActionState, useEffect, useRef } from "react";
import { createDealAction, type CreateDealState } from "./actions";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";

const initialState: CreateDealState = {};

// Previously there was no way anywhere in this app — backend or frontend —
// to create a Deal; only Seed.cs populated them. A self-serve-provisioned
// workspace (WorkspaceProvisioningService) got a Pipeline with Stages but
// could never put anything on it. See PipelineController.CreateDeal.
export function NewDealForm({
  stages,
  defaultCurrency,
}: {
  stages: { id: string; name: string }[];
  defaultCurrency: string;
}) {
  const [state, formAction, pending] = useActionState(createDealAction, initialState);
  const formRef = useRef<HTMLFormElement>(null);

  useEffect(() => {
    if (!pending && !state.error) formRef.current?.reset();
  }, [pending, state]);

  return (
    <form ref={formRef} action={formAction} className="flex flex-wrap items-end gap-3">
      <div className="space-y-1">
        <label htmlFor="dealCompanyName" className="text-xs font-medium text-neutral-600">
          Company
        </label>
        <Input id="dealCompanyName" name="companyName" required className="w-48" />
      </div>
      <div className="space-y-1">
        <label htmlFor="dealStageId" className="text-xs font-medium text-neutral-600">
          Stage
        </label>
        <select
          id="dealStageId"
          name="stageId"
          className="h-9 rounded-md border border-neutral-300 bg-white px-2 text-sm"
          defaultValue=""
        >
          <option value="">{stages[0]?.name ?? "First stage"}</option>
          {stages.slice(1).map((stage) => (
            <option key={stage.id} value={stage.id}>
              {stage.name}
            </option>
          ))}
        </select>
      </div>
      <div className="space-y-1">
        <label htmlFor="dealAmount" className="text-xs font-medium text-neutral-600">
          Amount ({defaultCurrency})
        </label>
        <Input id="dealAmount" name="amount" type="number" min="0" step="1" className="w-32" />
      </div>
      <Button type="submit" disabled={pending}>
        {pending ? "Adding…" : "New deal"}
      </Button>
      {state.error ? <p className="w-full text-sm text-red-600">{state.error}</p> : null}
    </form>
  );
}
