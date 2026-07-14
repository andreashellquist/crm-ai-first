import Link from "next/link";
import { notFound } from "next/navigation";
import { db } from "@/lib/db";
import { requireWorkspace } from "@/lib/workspace";
import { LogActivityForm } from "./log-activity-form";

function formatAmount(cents: number | null, currency: string | null) {
  if (cents == null) return null;
  return new Intl.NumberFormat("en-US", {
    style: "currency",
    currency: currency ?? "USD",
    maximumFractionDigits: 0,
  }).format(cents / 100);
}

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
  const { workspaceId } = await requireWorkspace();

  const deal = await db.deal.findFirst({
    where: { id: dealId, workspaceId, deletedAt: null },
    include: {
      stage: true,
      company: true,
      contacts: true,
      activities: { orderBy: { createdAt: "desc" } },
    },
  });
  if (!deal) notFound();

  return (
    <div className="max-w-2xl space-y-6">
      <div>
        <Link href="/pipeline" className="text-sm text-neutral-500 hover:text-neutral-950">
          ← Pipeline
        </Link>
        <h1 className="mt-1 text-xl font-semibold">{deal.company?.name ?? "Untitled deal"}</h1>
        <p className="text-sm text-neutral-500">
          {deal.stage.name} · {formatAmount(deal.amountCents, deal.currency) ?? "No amount set"}
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

      {deal.contacts.length > 0 ? (
        <div>
          <h2 className="text-sm font-semibold text-neutral-700">Contacts</h2>
          <ul className="mt-1 text-sm text-neutral-600">
            {deal.contacts.map((contact) => (
              <li key={contact.id}>
                {[contact.firstName, contact.lastName].filter(Boolean).join(" ") || contact.email}
              </li>
            ))}
          </ul>
        </div>
      ) : null}

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
                  <span>{activity.createdAt.toLocaleString()}</span>
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
