import Link from "next/link";
import { notFound } from "next/navigation";
import { requireWorkspace } from "@/lib/workspace";
import { formatAmount } from "@/lib/money";

// Previously didn't exist at all — global search results for contacts had
// nowhere to link to but the contacts list (see global-search.tsx). Kept
// deliberately read-only: editing/export/erasure already live on the
// contacts list row actions, and duplicating them here isn't this page's
// job.
export default async function ContactDetailPage({
  params,
}: {
  params: Promise<{ contactId: string }>;
}) {
  const { contactId } = await params;
  const { api } = await requireWorkspace();

  const { data: contact } = await api.GET("/api/contacts/{id}", { params: { path: { id: contactId } } });
  if (!contact) notFound();

  const { data: settings } = await api.GET("/api/workspace/settings");
  const defaultCurrency = settings?.defaultCurrency ?? "USD";

  const name = [contact.firstName, contact.lastName].filter(Boolean).join(" ") || "(no name)";

  return (
    <div className="max-w-2xl space-y-6">
      <div>
        <Link href="/contacts" className="text-sm text-neutral-500 hover:text-neutral-950">
          ← Contacts
        </Link>
        <h1 className="mt-1 text-xl font-semibold">{name}</h1>
        <p className="text-sm text-neutral-500">
          {contact.lifecycleStage}
          {contact.companyId && contact.companyName ? (
            <>
              {" · "}
              <Link href={`/companies/${contact.companyId}`} className="underline hover:text-neutral-950">
                {contact.companyName}
              </Link>
            </>
          ) : null}
        </p>
      </div>

      <div className="space-y-1 text-sm">
        <p>
          <span className="text-neutral-500">Email:</span> {contact.email ?? "—"}
        </p>
        <p>
          <span className="text-neutral-500">Phone:</span> {contact.phone ?? "—"}
        </p>
      </div>

      <div className="space-y-3">
        <h2 className="text-sm font-semibold text-neutral-700">Deals ({contact.deals.length})</h2>
        {contact.deals.length === 0 ? (
          <p className="text-sm text-neutral-500">No deals yet.</p>
        ) : (
          <ul className="space-y-2">
            {contact.deals.map((deal) => (
              <li key={deal.id} className="rounded border border-neutral-200 p-3 text-sm">
                <Link href={`/pipeline/${deal.id}`} className="font-medium text-neutral-800 underline hover:text-neutral-950">
                  {deal.title}
                </Link>
                <span className="ml-2 text-xs text-neutral-500">
                  {deal.stageName} · {formatAmount(deal.amountCents, deal.currency, defaultCurrency) ?? "No amount set"}
                </span>
              </li>
            ))}
          </ul>
        )}
      </div>

      <div className="space-y-3">
        <h2 className="text-sm font-semibold text-neutral-700">Activity ({contact.activities.length})</h2>
        {contact.activities.length === 0 ? (
          <p className="text-sm text-neutral-500">No activity logged yet.</p>
        ) : (
          <ul className="space-y-2">
            {contact.activities.map((activity) => (
              <li key={activity.id} className="rounded border border-neutral-200 p-3 text-sm">
                <div className="flex items-center justify-between text-xs text-neutral-500">
                  <span className="font-medium text-neutral-700">{activity.type}</span>
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
