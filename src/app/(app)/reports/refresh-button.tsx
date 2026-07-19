"use client";

import { useRef, useState } from "react";
import { useRouter } from "next/navigation";
import { refreshReportsAction, getRefreshJobStatusAction } from "./actions";
import { Button } from "@/components/ui/button";

const POLL_INTERVAL_MS = 1200;
const MAX_POLLS = 25;

// Reports refresh on the writes that change them (moving a deal, updating
// forecast/amount, logging an activity — see ReportingService), so this
// button exists for the gap that leaves: data that predates the feature or
// was seeded directly, which never triggered a refresh_reports job. See the
// reporting-read-models skill.
export function RefreshButton() {
  const router = useRouter();
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const stopPolling = useRef(false);

  async function handleRefresh() {
    setRefreshing(true);
    setError(null);
    stopPolling.current = false;

    let jobId: string;
    try {
      ({ jobId } = await refreshReportsAction());
    } catch {
      setRefreshing(false);
      setError("Could not start refresh — try again.");
      return;
    }

    for (let attempt = 0; attempt < MAX_POLLS && !stopPolling.current; attempt++) {
      await new Promise((resolve) => setTimeout(resolve, POLL_INTERVAL_MS));
      const result = await getRefreshJobStatusAction(jobId);

      if (result.status === "succeeded") {
        setRefreshing(false);
        router.refresh();
        return;
      }
      if (result.status === "failed") {
        setRefreshing(false);
        setError(result.error);
        return;
      }
    }

    if (!stopPolling.current) {
      setRefreshing(false);
      setError("Still working — check back in a moment.");
    }
  }

  return (
    <div className="flex items-center gap-2">
      <Button type="button" variant="outline" size="sm" disabled={refreshing} onClick={handleRefresh}>
        {refreshing ? "Refreshing…" : "Refresh"}
      </Button>
      {error ? <span className="text-xs text-red-600">{error}</span> : null}
    </div>
  );
}
