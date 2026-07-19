"use server";

import { requireWorkspace } from "@/lib/workspace";

export type SearchResultItem = { id: string; label: string; sublabel: string | null };
export type SearchResults = {
  contacts: SearchResultItem[];
  companies: SearchResultItem[];
  deals: SearchResultItem[];
};

const EMPTY_RESULTS: SearchResults = { contacts: [], companies: [], deals: [] };

export async function searchAction(q: string): Promise<SearchResults> {
  if (q.trim().length < 2) return EMPTY_RESULTS;
  const { api } = await requireWorkspace();
  const { data } = await api.GET("/api/search", { params: { query: { q } } });
  return data ?? EMPTY_RESULTS;
}
