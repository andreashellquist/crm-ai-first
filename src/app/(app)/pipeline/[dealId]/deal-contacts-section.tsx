"use client";

import { useRef, useState, useTransition } from "react";
import Link from "next/link";
import { addDealContactAction, removeDealContactAction } from "./actions";
import { Button } from "@/components/ui/button";

// Previously CreateDeal accepted contact ids only at creation time — there
// was no way to associate an existing Contact with an already-created Deal.
// See PipelineController.AddContact/RemoveContact.
export function DealContactsSection({
  dealId,
  contacts,
  allContacts,
}: {
  dealId: string;
  contacts: { id: string; name: string }[];
  allContacts: { id: string; name: string }[];
}) {
  const [pending, startTransition] = useTransition();
  const [error, setError] = useState<string | null>(null);
  const selectRef = useRef<HTMLSelectElement>(null);

  const associatedIds = new Set(contacts.map((c) => c.id));
  const addableContacts = allContacts.filter((c) => !associatedIds.has(c.id));

  return (
    <div>
      <h2 className="text-sm font-semibold text-neutral-700">Contacts</h2>
      {contacts.length > 0 ? (
        <ul className="mt-1 space-y-1 text-sm text-neutral-600">
          {contacts.map((contact) => (
            <li key={contact.id} className="flex items-center justify-between">
              <Link href={`/contacts/${contact.id}`} className="underline hover:text-neutral-950">
                {contact.name || "(no name)"}
              </Link>
              <Button
                type="button"
                variant="ghost"
                size="sm"
                disabled={pending}
                onClick={() => startTransition(() => removeDealContactAction(dealId, contact.id))}
              >
                Remove
              </Button>
            </li>
          ))}
        </ul>
      ) : (
        <p className="mt-1 text-sm text-neutral-500">No contacts associated yet.</p>
      )}

      {addableContacts.length > 0 ? (
        <div className="mt-2 flex items-center gap-2">
          <select
            ref={selectRef}
            aria-label="Add a contact"
            className="rounded border border-neutral-300 bg-white px-2 py-1 text-sm"
            defaultValue=""
          >
            <option value="" disabled>
              Add a contact…
            </option>
            {addableContacts.map((contact) => (
              <option key={contact.id} value={contact.id}>
                {contact.name || contact.id}
              </option>
            ))}
          </select>
          <Button
            type="button"
            variant="outline"
            size="sm"
            disabled={pending}
            onClick={() => {
              const contactId = selectRef.current?.value;
              if (!contactId) return;
              setError(null);
              startTransition(async () => {
                try {
                  await addDealContactAction(dealId, contactId);
                } catch {
                  setError("Could not add that contact.");
                }
              });
            }}
          >
            Add
          </Button>
        </div>
      ) : null}
      {error ? <p className="mt-1 text-xs text-red-600">{error}</p> : null}
    </div>
  );
}
