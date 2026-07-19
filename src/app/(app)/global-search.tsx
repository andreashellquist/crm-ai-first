"use client";

import { useEffect, useRef, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { searchAction, type SearchResults } from "./search-actions";

const DEBOUNCE_MS = 250;

// Deals have a real detail page (/pipeline/{id}) to link to. Contacts and
// companies don't have their own detail pages in this app yet — clicking a
// contact/company result instead lands on the contacts list pre-filtered by
// the same search term (ContactsController.List's `q` param matches the
// same fields this search does), which at least surfaces the record rather
// than going nowhere. Revisit once contact/company detail pages exist.
export function GlobalSearch() {
  const router = useRouter();
  const [query, setQuery] = useState("");
  const [results, setResults] = useState<SearchResults | null>(null);
  const [open, setOpen] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => {
    function handleClickOutside(event: MouseEvent) {
      if (containerRef.current && !containerRef.current.contains(event.target as Node)) {
        setOpen(false);
      }
    }
    document.addEventListener("mousedown", handleClickOutside);
    return () => document.removeEventListener("mousedown", handleClickOutside);
  }, []);

  function handleChange(value: string) {
    setQuery(value);
    setOpen(true);
    if (debounceRef.current) clearTimeout(debounceRef.current);
    if (value.trim().length < 2) {
      setResults(null);
      return;
    }
    debounceRef.current = setTimeout(async () => {
      const r = await searchAction(value);
      setResults(r);
    }, DEBOUNCE_MS);
  }

  function contactsListHref() {
    return `/contacts?q=${encodeURIComponent(query)}`;
  }

  function handleResultClick() {
    setOpen(false);
    setQuery("");
    setResults(null);
  }

  const hasResults = results && (results.contacts.length > 0 || results.companies.length > 0 || results.deals.length > 0);

  return (
    <div ref={containerRef} className="relative w-64">
      <input
        type="search"
        value={query}
        onChange={(event) => handleChange(event.target.value)}
        onFocus={() => setOpen(true)}
        onKeyDown={(event) => {
          if (event.key === "Enter" && query.trim().length >= 2) {
            router.push(contactsListHref());
            handleResultClick();
          }
        }}
        placeholder="Search contacts, companies, deals…"
        className="w-full rounded-md border border-neutral-300 px-3 py-1.5 text-sm placeholder:text-neutral-400"
      />

      {open && query.trim().length >= 2 ? (
        <div className="absolute left-0 z-10 mt-1 w-96 rounded-md border border-neutral-200 bg-white shadow-lg">
          {!results ? <p className="p-3 text-sm text-neutral-500">Searching…</p> : null}
          {results && !hasResults ? <p className="p-3 text-sm text-neutral-500">No matches.</p> : null}
          {results?.deals.length ? (
            <ResultGroup title="Deals">
              {results.deals.map((r) => (
                <Link key={r.id} href={`/pipeline/${r.id}`} onClick={handleResultClick} className="block px-3 py-2 text-sm hover:bg-neutral-50">
                  <span className="text-neutral-800">{r.label}</span>
                  {r.sublabel ? <span className="ml-1 text-xs text-neutral-500">{r.sublabel}</span> : null}
                </Link>
              ))}
            </ResultGroup>
          ) : null}
          {results?.contacts.length ? (
            <ResultGroup title="Contacts">
              {results.contacts.map((r) => (
                <Link key={r.id} href={contactsListHref()} onClick={handleResultClick} className="block px-3 py-2 text-sm hover:bg-neutral-50">
                  <span className="text-neutral-800">{r.label}</span>
                  {r.sublabel ? <span className="ml-1 text-xs text-neutral-500">{r.sublabel}</span> : null}
                </Link>
              ))}
            </ResultGroup>
          ) : null}
          {results?.companies.length ? (
            <ResultGroup title="Companies">
              {results.companies.map((r) => (
                <Link key={r.id} href={contactsListHref()} onClick={handleResultClick} className="block px-3 py-2 text-sm hover:bg-neutral-50">
                  <span className="text-neutral-800">{r.label}</span>
                  {r.sublabel ? <span className="ml-1 text-xs text-neutral-500">{r.sublabel}</span> : null}
                </Link>
              ))}
            </ResultGroup>
          ) : null}
        </div>
      ) : null}
    </div>
  );
}

function ResultGroup({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className="border-b border-neutral-50 py-1 last:border-0">
      <p className="px-3 py-1 text-xs font-medium uppercase text-neutral-500">{title}</p>
      {children}
    </div>
  );
}
