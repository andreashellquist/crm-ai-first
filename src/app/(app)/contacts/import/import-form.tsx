"use client";

import { useRef, useState } from "react";
import { useRouter } from "next/navigation";
import {
  previewImportAction,
  startImportAction,
  getImportJobStatusAction,
  type ImportPreview,
  type ImportSummary,
} from "./actions";
import { Button } from "@/components/ui/button";

const IMPORTABLE_FIELDS: { key: string; label: string }[] = [
  { key: "email", label: "Email" },
  { key: "firstName", label: "First name" },
  { key: "lastName", label: "Last name" },
  { key: "phone", label: "Phone" },
  { key: "companyName", label: "Company name" },
  { key: "companyDomain", label: "Company domain" },
];

// Best-effort auto-mapping so the common case ("Email", "First Name", ...)
// needs no manual work — the user can always override via the <select>s.
const HEADER_ALIASES: Record<string, string[]> = {
  email: ["email", "emailaddress", "e-mail"],
  firstName: ["firstname", "first", "fname", "givenname"],
  lastName: ["lastname", "last", "lname", "surname", "familyname"],
  phone: ["phone", "phonenumber", "mobile", "telephone"],
  companyName: ["company", "companyname", "organization", "org"],
  companyDomain: ["domain", "companydomain", "website", "url"],
};

function normalizeHeader(header: string) {
  return header.toLowerCase().replace(/[\s_-]/g, "");
}

function autoMapColumns(headers: string[]): Record<string, string> {
  const mapping: Record<string, string> = {};
  for (const [field, aliases] of Object.entries(HEADER_ALIASES)) {
    const match = headers.find((h) => aliases.includes(normalizeHeader(h)));
    if (match) mapping[field] = match;
  }
  return mapping;
}

const POLL_INTERVAL_MS = 1200;
const MAX_POLLS = 60; // imports can run longer than the single-record AI-feature polls

// CSV parsing, dedup, and persistence all happen server-side (backend/CrmApi/
// Services/ContactImportService.cs) — this component only reads the file into
// a string and forwards it, per the "frontend holds no business logic"
// convention. See the csv-import-dedupe skill.
export function ImportForm() {
  const router = useRouter();
  const fileInputRef = useRef<HTMLInputElement>(null);
  const stopPolling = useRef(false);

  const [csvContent, setCsvContent] = useState<string | null>(null);
  const [fileName, setFileName] = useState<string | null>(null);
  const [preview, setPreview] = useState<ImportPreview | null>(null);
  const [mapping, setMapping] = useState<Record<string, string>>({});
  const [loadingPreview, setLoadingPreview] = useState(false);
  const [importing, setImporting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [summary, setSummary] = useState<ImportSummary | null>(null);

  async function handleFileChange(event: React.ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0];
    if (!file) return;
    setError(null);
    setSummary(null);
    setPreview(null);
    setFileName(file.name);
    setLoadingPreview(true);

    const text = await file.text();
    setCsvContent(text);

    try {
      const result = await previewImportAction(text);
      setPreview(result);
      setMapping(autoMapColumns(result.headers));
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not read that CSV file");
    } finally {
      setLoadingPreview(false);
    }
  }

  async function handleImport() {
    if (!csvContent) return;
    if (!mapping.email && !mapping.firstName && !mapping.lastName) {
      setError("Map at least Email, First name, or Last name before importing");
      return;
    }
    setImporting(true);
    setError(null);
    setSummary(null);
    stopPolling.current = false;

    let jobId: string;
    try {
      ({ jobId } = await startImportAction(csvContent, mapping));
    } catch {
      setImporting(false);
      setError("Could not start the import — try again.");
      return;
    }

    for (let attempt = 0; attempt < MAX_POLLS && !stopPolling.current; attempt++) {
      await new Promise((resolve) => setTimeout(resolve, POLL_INTERVAL_MS));
      const result = await getImportJobStatusAction(jobId);

      if (result.status === "succeeded") {
        setImporting(false);
        setSummary(result.summary);
        router.refresh();
        return;
      }
      if (result.status === "failed") {
        setImporting(false);
        setError(result.error);
        return;
      }
      // "pending" / "processing" / "not_found" (job not yet visible) — keep polling
    }

    if (!stopPolling.current) {
      setImporting(false);
      setError("Still working — check back in a moment.");
    }
  }

  function reset() {
    setCsvContent(null);
    setFileName(null);
    setPreview(null);
    setMapping({});
    setSummary(null);
    setError(null);
    if (fileInputRef.current) fileInputRef.current.value = "";
  }

  return (
    <div className="space-y-6">
      <div>
        <label className="block text-sm font-medium text-neutral-700">CSV file</label>
        <input
          ref={fileInputRef}
          type="file"
          accept=".csv,text/csv"
          onChange={handleFileChange}
          disabled={loadingPreview || importing}
          className="mt-1 block text-sm"
        />
        {fileName ? <p className="mt-1 text-xs text-neutral-500">{fileName}</p> : null}
      </div>

      {error ? <p className="text-sm text-red-600">{error}</p> : null}
      {loadingPreview ? <p className="text-sm text-neutral-500">Reading file…</p> : null}

      {preview ? (
        <div className="space-y-4">
          <div>
            <h2 className="text-sm font-semibold text-neutral-700">Map columns</h2>
            <p className="text-xs text-neutral-500">
              {preview.totalRows} row{preview.totalRows === 1 ? "" : "s"} detected. Map at least one of Email, First
              name, or Last name.
            </p>
            <div className="mt-2 grid grid-cols-2 gap-3 sm:grid-cols-3">
              {IMPORTABLE_FIELDS.map((field) => (
                <label key={field.key} className="block text-xs font-medium text-neutral-600">
                  {field.label}
                  <select
                    className="mt-1 w-full rounded border border-neutral-300 bg-white px-2 py-1 text-sm"
                    value={mapping[field.key] ?? ""}
                    disabled={importing}
                    onChange={(event) =>
                      setMapping((prev) => {
                        const next = { ...prev };
                        if (event.target.value) next[field.key] = event.target.value;
                        else delete next[field.key];
                        return next;
                      })
                    }
                  >
                    <option value="">— don&apos;t import —</option>
                    {preview.headers.map((h) => (
                      <option key={h} value={h}>
                        {h}
                      </option>
                    ))}
                  </select>
                </label>
              ))}
            </div>
          </div>

          <div>
            <h2 className="text-sm font-semibold text-neutral-700">Preview (first {preview.previewRows.length})</h2>
            <div className="mt-1 overflow-x-auto rounded border border-neutral-200">
              <table className="w-full text-xs">
                <thead className="bg-neutral-50 text-left uppercase text-neutral-500">
                  <tr>
                    {preview.headers.map((h) => (
                      <th key={h} className="px-2 py-1 font-medium">
                        {h}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {preview.previewRows.map((row, i) => (
                    <tr key={i} className="border-t border-neutral-100">
                      {preview.headers.map((h) => (
                        <td key={h} className="px-2 py-1 text-neutral-600">
                          {row[h]}
                        </td>
                      ))}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>

          <div className="flex items-center gap-2">
            <Button type="button" disabled={importing} onClick={handleImport}>
              {importing ? "Importing…" : `Import ${preview.totalRows} contact${preview.totalRows === 1 ? "" : "s"}`}
            </Button>
            <Button type="button" variant="outline" disabled={importing} onClick={reset}>
              Start over
            </Button>
          </div>
        </div>
      ) : null}

      {summary ? (
        <div className="rounded border border-neutral-200 bg-neutral-50 p-3 text-sm">
          <p className="font-medium text-neutral-800">
            {summary.created} created, {summary.updated} updated, {summary.skipped} skipped
            {summary.errors.length > 0
              ? `, ${summary.errors.length} error${summary.errors.length === 1 ? "" : "s"}`
              : ""}
            .
          </p>
          {summary.errors.length > 0 ? (
            <ul className="mt-2 space-y-1 text-xs text-red-600">
              {summary.errors.slice(0, 20).map((e) => (
                <li key={e.row}>
                  Row {e.row}: {e.message}
                </li>
              ))}
              {summary.errors.length > 20 ? <li>…and {summary.errors.length - 20} more</li> : null}
            </ul>
          ) : null}
        </div>
      ) : null}
    </div>
  );
}
