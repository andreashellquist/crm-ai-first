"use client";

import { useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { createSavedViewAction, deleteSavedViewAction, type SavedView } from "./actions";
import { Button } from "@/components/ui/button";

export function SavedViewsBar({ views }: { views: SavedView[] }) {
  const router = useRouter();
  const searchParams = useSearchParams();
  const [saving, setSaving] = useState(false);
  const [name, setName] = useState("");
  const [error, setError] = useState<string | null>(null);

  async function handleSave() {
    if (!name.trim()) {
      setError("Name this view first");
      return;
    }
    setError(null);
    try {
      await createSavedViewAction("contact", name.trim(), searchParams.toString());
      setName("");
      setSaving(false);
      router.refresh();
    } catch {
      setError("Could not save this view");
    }
  }

  async function handleDelete(id: string) {
    await deleteSavedViewAction(id);
    router.refresh();
  }

  return (
    <div className="flex flex-wrap items-center gap-2 text-sm">
      {views.map((view) => (
        <div key={view.id} className="flex items-center gap-1 rounded-full border border-neutral-200 bg-neutral-50 pl-3 pr-1">
          <a href={`/contacts?${view.queryString}`} className="py-1 text-neutral-700 hover:text-neutral-950">
            {view.name}
          </a>
          <button
            type="button"
            onClick={() => handleDelete(view.id)}
            aria-label={`Delete saved view "${view.name}"`}
            className="rounded-full px-1.5 text-neutral-500 hover:bg-neutral-200 hover:text-neutral-700"
          >
            ×
          </button>
        </div>
      ))}

      {saving ? (
        <div className="flex items-center gap-1">
          <input
            autoFocus
            value={name}
            onChange={(event) => setName(event.target.value)}
            onKeyDown={(event) => event.key === "Enter" && handleSave()}
            placeholder="View name"
            className="rounded-md border border-neutral-300 px-2 py-1 text-xs"
          />
          <Button type="button" size="sm" onClick={handleSave}>
            Save
          </Button>
          <Button type="button" size="sm" variant="ghost" onClick={() => { setSaving(false); setError(null); }}>
            Cancel
          </Button>
        </div>
      ) : (
        <button type="button" onClick={() => setSaving(true)} className="text-xs text-neutral-500 underline hover:text-neutral-900">
          Save this view
        </button>
      )}
      {error ? <span className="text-xs text-red-600">{error}</span> : null}
    </div>
  );
}
