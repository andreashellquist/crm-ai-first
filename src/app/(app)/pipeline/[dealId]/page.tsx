import Link from "next/link";
import { notFound } from "next/navigation";
import { requireWorkspace } from "@/lib/workspace";
import { formatAmount } from "@/lib/money";
import { LogActivityForm } from "./log-activity-form";
import { DraftEmail } from "./draft-email";
import { DealSummary } from "./deal-summary";
import { NextBestAction } from "./next-best-action";
import { ListingPanel } from "./listing-panel";

const ACTIVITY_LABELS: Record<string, string> = {
  call: "Call",
  email: "Email",
  meeting: "Meeting",
  note: "Note",
};

export default async function DealDetailPage({
  params,
}: {
  params: Promise<{ dealId: string }>;
}) {
  const { dealId } = await params;
  const { api } = await requireWorkspace();

  const { data: deal } = await api.GET("/api/deals/{dealId}", { params: { path: { dealId } } });
  if (!deal) notFound();

  // Optional module (workspace-customization skill §4) — only fetched/shown
  // when this workspace has "listings" enabled; absent entirely otherwise,
  // proving core deal-detail flows don't depend on any module.
  const { data: settings } = await api.GET("/api/workspace/settings");
  const listingsEnabled = settings?.enabledModules?.includes("listings") ?? false;
  const listing = listingsEnabled
    ? (await api.GET("/api/deals/{dealId}/listing", { params: { path: { dealId } } })).data
    : null;

  return (
    <div className="max-w-2xl space-y-6">
      <div>
        <Link href="/pipeline" className="text-sm text-neutral-500 hover:text-neutral-950">
          ← Pipeline
        </Link>
        <h1 className="mt-1 text-xl font-semibold">{deal.title}</h1>
        <p className="text-sm text-neutral-500">
          {deal.stageName} · {formatAmount(deal.amountCents, deal.currency) ?? "No amount set"}
        </p>
      </div>

      {deal.aiScore != null ? (
        <div className="rounded border border-indigo-200 bg-indigo-50 p-3 text-sm">
          <div className="flex items-center gap-1 font-medium text-indigo-700">
            <span aria-hidden>✦</span> AI score: {deal.aiScore}/100
          </div>
          {deal.aiScoreRationale ? <p className="mt-1 text-indigo-900">{deal.aiScoreRationale}</p> : null}
        </div>
      ) : null}

      <DealSummary
        dealId={deal.id}
        summary={deal.aiSummary ?? null}
        summarizedAt={deal.aiSummarizedAt ?? null}
        activitiesSinceSummary={Number(deal.activitiesSinceSummary)}
      />

      {deal.contactNames.length > 0 ? (
        <div>
          <h2 className="text-sm font-semibold text-neutral-700">Contacts</h2>
          <ul className="mt-1 text-sm text-neutral-600">
            {deal.contactNames.map((name) => (
              <li key={name}>{name}</li>
            ))}
          </ul>
        </div>
      ) : null}

      <NextBestAction dealId={deal.id} />

      {listingsEnabled ? (
        <ListingPanel
          dealId={deal.id}
          listing={{
            listingAgentName: listing?.listingAgentName ?? null,
            listingUrl: listing?.listingUrl ?? null,
            openHouseAt: listing?.openHouseAt ?? null,
            commissionPercent: listing?.commissionPercent ?? null,
          }}
        />
      ) : null}

      <DraftEmail dealId={deal.id} />

      <div className="space-y-3">
        <h2 className="text-sm font-semibold text-neutral-700">Log activity</h2>
        <LogActivityForm dealId={deal.id} />
      </div>

      <div className="space-y-3">
        <h2 className="text-sm font-semibold text-neutral-700">Activity ({deal.activities.length})</h2>
        {deal.activities.length === 0 ? (
          <p className="text-sm text-neutral-400">No activity logged yet.</p>
        ) : (
          <ul className="space-y-2">
            {deal.activities.map((activity) => (
              <li key={activity.id} className="rounded border border-neutral-200 p-3 text-sm">
                <div className="flex items-center justify-between text-xs text-neutral-500">
                  <span className="font-medium text-neutral-700">
                    {ACTIVITY_LABELS[activity.type] ?? activity.type}
                  </span>
                  <span>{new Date(activity.createdAt).toLocaleString()}</span>
                </div>
                {activity.body ? <p className="mt-1 text-neutral-800">{activity.body}</p> : null}
              </li>
            ))}
          </ul>
        )}
      </div>
    </div>
  );
}
