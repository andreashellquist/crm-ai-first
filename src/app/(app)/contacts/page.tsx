import Link from "next/link";
import { requireWorkspace } from "@/lib/workspace";
import { NewContactForm } from "./new-contact-form";
import { ContactFilters } from "./contact-filters";
import { SavedViewsBar } from "./saved-views-bar";
import { listSavedViewsAction } from "./actions";

export default async function ContactsPage({
  searchParams,
}: {
  searchParams: Promise<{ q?: string; lifecycleStage?: string; sort?: string }>;
}) {
  const { q = "", lifecycleStage = "", sort = "-createdAt" } = await searchParams;
  const { api } = await requireWorkspace();
  const [{ data }, savedViews] = await Promise.all([
    api.GET("/api/contacts", { params: { query: { q: q || undefined, lifecycleStage: lifecycleStage || undefined, sort } } }),
    listSavedViewsAction("contact"),
  ]);
  const contacts = data ?? [];

  return (
    <div className="space-y-6">
      <div className="flex items-baseline justify-between">
        <div>
          <h1 className="text-xl font-semibold">Contacts</h1>
          <p className="text-sm text-neutral-500">{contacts.length} shown</p>
        </div>
        <Link href="/contacts/import" className="text-sm text-neutral-600 underline hover:text-neutral-950">
          Import CSV
        </Link>
      </div>

      <NewContactForm />

      <div className="space-y-3 border-t border-neutral-100 pt-4">
        <ContactFilters q={q} lifecycleStage={lifecycleStage} sort={sort} />
        <SavedViewsBar views={savedViews} />
      </div>

      <div className="overflow-hidden rounded-lg border border-neutral-200">
        <table className="w-full text-sm">
          <thead className="bg-neutral-50 text-left text-xs uppercase text-neutral-500">
            <tr>
              <th className="px-4 py-2 font-medium">Name</th>
              <th className="px-4 py-2 font-medium">Email</th>
              <th className="px-4 py-2 font-medium">Company</th>
              <th className="px-4 py-2 font-medium">Lifecycle stage</th>
            </tr>
          </thead>
          <tbody>
            {contacts.map((contact) => (
              <tr key={contact.id} className="border-t border-neutral-100">
                <td className="px-4 py-2">
                  {[contact.firstName, contact.lastName].filter(Boolean).join(" ") || "—"}
                </td>
                <td className="px-4 py-2 text-neutral-600">{contact.email ?? "—"}</td>
                <td className="px-4 py-2 text-neutral-600">{contact.companyName ?? "—"}</td>
                <td className="px-4 py-2 text-neutral-600">{contact.lifecycleStage}</td>
              </tr>
            ))}
            {contacts.length === 0 ? (
              <tr>
                <td colSpan={4} className="px-4 py-6 text-center text-neutral-500">
                  No contacts match — try adjusting the filters above.
                </td>
              </tr>
            ) : null}
          </tbody>
        </table>
      </div>
    </div>
  );
}
