"use client";

const LIFECYCLE_STAGES = ["subscriber", "lead", "mql", "sql", "opportunity", "customer", "churned"];

// Plain HTML GET form — filters live entirely in the URL (shareable/
// bookmarkable, per frontend-engineer's convention), so navigating here
// works with JS disabled too. The only client-side behavior is auto-
// submitting when a <select> changes, so picking a stage/sort doesn't
// require a separate click.
export function ContactFilters({
  q,
  lifecycleStage,
  sort,
}: {
  q: string;
  lifecycleStage: string;
  sort: string;
}) {
  return (
    <form method="GET" className="flex flex-wrap items-end gap-3">
      <div className="space-y-1">
        <label htmlFor="q" className="text-xs font-medium text-neutral-600">
          Search
        </label>
        <input
          id="q"
          name="q"
          defaultValue={q}
          placeholder="Name or email"
          className="w-56 rounded-md border border-neutral-300 px-3 py-1.5 text-sm"
        />
      </div>
      <div className="space-y-1">
        <label htmlFor="lifecycleStage" className="text-xs font-medium text-neutral-600">
          Lifecycle stage
        </label>
        <select
          id="lifecycleStage"
          name="lifecycleStage"
          defaultValue={lifecycleStage}
          onChange={(event) => event.currentTarget.form?.requestSubmit()}
          className="rounded-md border border-neutral-300 bg-white px-2 py-1.5 text-sm"
        >
          <option value="">All stages</option>
          {LIFECYCLE_STAGES.map((stage) => (
            <option key={stage} value={stage}>
              {stage}
            </option>
          ))}
        </select>
      </div>
      <div className="space-y-1">
        <label htmlFor="sort" className="text-xs font-medium text-neutral-600">
          Sort
        </label>
        <select
          id="sort"
          name="sort"
          defaultValue={sort}
          onChange={(event) => event.currentTarget.form?.requestSubmit()}
          className="rounded-md border border-neutral-300 bg-white px-2 py-1.5 text-sm"
        >
          <option value="-createdAt">Newest first</option>
          <option value="createdAt">Oldest first</option>
          <option value="name">Name A-Z</option>
          <option value="-name">Name Z-A</option>
        </select>
      </div>
      <button type="submit" className="rounded-md border border-neutral-300 px-3 py-1.5 text-sm hover:bg-neutral-50">
        Apply
      </button>
    </form>
  );
}
