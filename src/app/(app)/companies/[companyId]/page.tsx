import Link from "next/link";
import { notFound } from "next/navigation";
import { requireWorkspace } from "@/lib/workspace";
import { formatAmount } from "@/lib/money";

// The first company detail page anywhere in this app — CompaniesController
// has had List/Get/Update since Phase 0-1, but nothing in the frontend ever
// called Get until now. Global search results for companies had nowhere to
// link to but the contacts list.
export default async function CompanyDetailPage({
  params,
}: {
  params: Promise<{ companyId: string }>;
}) {
  const { companyId } = await params;
  const { api } = await requireWorkspace();

  const { data: company } = await api.GET("/api/companies/{id}", { params: { path: { id: companyId } } });
  if (!company) notFound();

  const { data: settings } = await api.GET("/api/workspace/settings");
  const defaultCurrency = settings?.defaultCurrency ?? "USD";

  return (
    <div className="max-w-2xl space-y-6">
      <div>
        <Link href="/contacts" className="text-sm text-neutral-500 hover:text-neutral-950">
          ← Contacts
        </Link>
        <h1 className="mt-1 text-xl font-semibold">{company.name}</h1>
        {company.domain ? <p className="text-sm text-neutral-500">{company.domain}</p> : null}
      </div>

      <div className="space-y-3">
        <h2 className="text-sm font-semibold text-neutral-700">Contacts ({company.contacts.length})</h2>
        {company.contacts.length === 0 ? (
          <p className="text-sm text-neutral-500">No contacts yet.</p>
        ) : (
          <ul className="space-y-1 text-sm">
            {company.contacts.map((contact) => (
              <li key={contact.id}>
                <Link href={`/contacts/${contact.id}`} className="text-neutral-800 underline hover:text-neutral-950">
                  {contact.name || "(no name)"}
                </Link>
              </li>
            ))}
          </ul>
        )}
      </div>

      <div className="space-y-3">
        <h2 className="text-sm font-semibold text-neutral-700">Deals ({company.deals.length})</h2>
        {company.deals.length === 0 ? (
          <p className="text-sm text-neutral-500">No deals yet.</p>
        ) : (
          <ul className="space-y-2">
            {company.deals.map((deal) => (
              <li key={deal.id} className="rounded border border-neutral-200 p-3 text-sm">
                <Link href={`/pipeline/${deal.id}`} className="font-medium text-neutral-800 underline hover:text-neutral-950">
                  {deal.stageName}
                </Link>
                <span className="ml-2 text-xs text-neutral-500">
                  {formatAmount(deal.amountCents, deal.currency, defaultCurrency) ?? "No amount set"}
                </span>
              </li>
            ))}
          </ul>
        )}
      </div>
    </div>
  );
}
