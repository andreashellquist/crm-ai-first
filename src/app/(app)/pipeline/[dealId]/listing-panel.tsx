"use client";

import { useActionState } from "react";
import { saveListingAction, type SaveListingState } from "./actions";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";

const initialState: SaveListingState = {};

export function ListingPanel({
  dealId,
  listing,
}: {
  dealId: string;
  listing: {
    listingAgentName: string | null;
    listingUrl: string | null;
    openHouseAt: string | null;
    commissionPercent: number | string | null;
  };
}) {
  const [state, formAction, pending] = useActionState(saveListingAction, initialState);

  return (
    <div className="space-y-3 rounded-lg border border-neutral-200 p-3">
      <h2 className="text-sm font-semibold text-neutral-700">Listing</h2>
      <form action={formAction} className="grid grid-cols-1 gap-2 sm:grid-cols-2">
        <input type="hidden" name="dealId" value={dealId} />

        <div className="space-y-1">
          <label htmlFor="listingAgentName" className="text-xs font-medium text-neutral-600">
            Listing agent
          </label>
          <Input id="listingAgentName" name="listingAgentName" defaultValue={listing.listingAgentName ?? ""} />
        </div>

        <div className="space-y-1">
          <label htmlFor="listingUrl" className="text-xs font-medium text-neutral-600">
            Listing URL
          </label>
          <Input id="listingUrl" name="listingUrl" type="url" defaultValue={listing.listingUrl ?? ""} />
        </div>

        <div className="space-y-1">
          <label htmlFor="openHouseAt" className="text-xs font-medium text-neutral-600">
            Open house
          </label>
          <Input
            id="openHouseAt"
            name="openHouseAt"
            type="datetime-local"
            defaultValue={listing.openHouseAt ? listing.openHouseAt.slice(0, 16) : ""}
          />
        </div>

        <div className="space-y-1">
          <label htmlFor="commissionPercent" className="text-xs font-medium text-neutral-600">
            Commission %
          </label>
          <Input
            id="commissionPercent"
            name="commissionPercent"
            type="number"
            min={0}
            max={100}
            step="0.1"
            defaultValue={listing.commissionPercent ?? ""}
          />
        </div>

        {state.error ? <p className="text-sm text-red-600 sm:col-span-2">{state.error}</p> : null}

        <div className="sm:col-span-2">
          <Button type="submit" size="sm" disabled={pending}>
            {pending ? "Saving…" : "Save listing"}
          </Button>
        </div>
      </form>
    </div>
  );
}
