"use client";

import { useState, useTransition } from "react";
import { eraseContactAction } from "./actions";
import { Button } from "@/components/ui/button";

// Data-subject request fulfillment — "Export" downloads everything this CRM
// holds on the contact (ContactsController.Export); "Erase" anonymizes their
// PII in place (ContactsController.Erase). Both are owner/admin-only
// server-side; shown to every member here (same convention as the workspace
// settings forms) with the resulting error surfaced inline rather than
// hidden based on a client-guessed role.
export function ContactRowActions({ contactId, label }: { contactId: string; label: string }) {
  const [error, setError] = useState<string | null>(null);
  const [pending, startTransition] = useTransition();

  return (
    <div className="flex items-center justify-end gap-2">
      <a
        href={`/api/contacts/${contactId}/export`}
        className="text-xs text-neutral-500 underline hover:text-neutral-900"
      >
        Export
      </a>
      <Button
        type="button"
        variant="ghost"
        size="sm"
        disabled={pending}
        onClick={() =>
          startTransition(async () => {
            setError(null);
            const result = await eraseContactAction(contactId);
            if (result) setError(result);
          })
        }
        aria-label={`Erase ${label}'s data`}
      >
        {pending ? "Erasing…" : "Erase"}
      </Button>
      {error ? <span className="text-xs text-red-600">{error}</span> : null}
    </div>
  );
}
