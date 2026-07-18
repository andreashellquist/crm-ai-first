import { requireWorkspace } from "@/lib/workspace";
import { NewContactForm } from "./new-contact-form";

export default async function ContactsPage() {
  const { api } = await requireWorkspace();
  const { data } = await api.GET("/api/contacts");
  const contacts = data ?? [];

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold">Contacts</h1>
        <p className="text-sm text-neutral-500">{contacts.length} total</p>
      </div>

      <NewContactForm />

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
                <td colSpan={4} className="px-4 py-6 text-center text-neutral-400">
                  No contacts yet — add the first one above.
                </td>
              </tr>
            ) : null}
          </tbody>
        </table>
      </div>
    </div>
  );
}
