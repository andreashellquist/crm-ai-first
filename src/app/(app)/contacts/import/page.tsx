import Link from "next/link";
import { ImportForm } from "./import-form";

export default function ImportContactsPage() {
  return (
    <div className="max-w-3xl space-y-6">
      <div>
        <Link href="/contacts" className="text-sm text-neutral-500 hover:text-neutral-950">
          ← Contacts
        </Link>
        <h1 className="mt-1 text-xl font-semibold">Import contacts</h1>
        <p className="text-sm text-neutral-500">
          Upload a CSV, map its columns, and preview before importing. Existing contacts are matched by email (or by
          company + name) and only filled in where blank — nothing is overwritten.
        </p>
      </div>

      <ImportForm />
    </div>
  );
}
