import { requireWorkspace } from "@/lib/workspace";
import { formatAmount } from "@/lib/money";
import { RefreshButton } from "./refresh-button";

const FORECAST_LABELS: Record<string, string> = {
  pipeline: "Pipeline",
  best_case: "Best case",
  commit: "Commit",
  closed: "Closed",
};

const ACTIVITY_LABELS: Record<string, string> = {
  call: "Calls",
  email: "Emails",
  meeting: "Meetings",
  note: "Notes",
};

function Bar({ value, max }: { value: number; max: number }) {
  const pct = max > 0 ? Math.max((value / max) * 100, value > 0 ? 2 : 0) : 0;
  return (
    <div className="h-2 w-full rounded-full bg-neutral-100">
      <div className="h-2 rounded-full bg-indigo-500" style={{ width: `${pct}%` }} />
    </div>
  );
}

export default async function ReportsPage() {
  const { api } = await requireWorkspace();
  const { data: report } = await api.GET("/api/reports");

  if (!report) {
    return <p className="text-sm text-neutral-500">No pipeline configured for this workspace yet.</p>;
  }

  const maxPipelineValue = Math.max(...report.pipeline.map((r) => Number(r.dealValueCents)), 0);
  const maxForecastValue = Math.max(...report.forecast.map((r) => Number(r.dealValueCents)), 0);

  const activityTotals = new Map<string, number>();
  for (const row of report.activity) {
    activityTotals.set(row.type, (activityTotals.get(row.type) ?? 0) + Number(row.count));
  }
  const activityRows = [...activityTotals.entries()].sort((a, b) => b[1] - a[1]);
  const maxActivityCount = Math.max(...activityRows.map(([, count]) => count), 0);

  return (
    <div className="max-w-4xl space-y-8">
      <div className="flex items-baseline justify-between">
        <div>
          <h1 className="text-xl font-semibold">Reports</h1>
          <p className="text-sm text-neutral-500">
            {report.refreshedAtDisplay
              ? `Last refreshed ${report.refreshedAtDisplay}`
              : "Not refreshed yet — add or update a deal, or click Refresh."}
          </p>
        </div>
        <RefreshButton />
      </div>

      <section className="space-y-3">
        <div className="flex items-baseline justify-between">
          <h2 className="text-sm font-semibold text-neutral-700">
            {report.dealTermPlural.charAt(0).toUpperCase() + report.dealTermPlural.slice(1)} by stage
          </h2>
          <a href="/api/reports/export?type=pipeline" className="text-xs text-neutral-500 underline hover:text-neutral-900">
            Export CSV
          </a>
        </div>
        <div className="overflow-hidden rounded-lg border border-neutral-200">
          <table className="w-full text-sm">
            <thead className="bg-neutral-50 text-left text-xs uppercase text-neutral-500">
              <tr>
                <th className="px-4 py-2 font-medium">Stage</th>
                <th className="px-4 py-2 font-medium">{report.dealTermPlural}</th>
                <th className="w-1/3 px-4 py-2 font-medium">Value</th>
                <th className="px-4 py-2 font-medium">Weighted</th>
              </tr>
            </thead>
            <tbody>
              {report.pipeline.map((row) => (
                <tr key={row.stageId} className="border-t border-neutral-100">
                  <td className="px-4 py-2">{row.stageName}</td>
                  <td className="px-4 py-2 text-neutral-600">{row.dealCount}</td>
                  <td className="px-4 py-2">
                    <div className="flex items-center gap-2">
                      <Bar value={Number(row.dealValueCents)} max={maxPipelineValue} />
                      <span className="whitespace-nowrap text-xs text-neutral-500">{formatAmount(row.dealValueCents, report.defaultCurrency, report.defaultCurrency)}</span>
                    </div>
                  </td>
                  <td className="px-4 py-2 text-neutral-600">{formatAmount(row.weightedValueCents, report.defaultCurrency, report.defaultCurrency)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      <section className="space-y-3">
        <div className="flex items-baseline justify-between">
          <h2 className="text-sm font-semibold text-neutral-700">Forecast by category</h2>
          <a href="/api/reports/export?type=forecast" className="text-xs text-neutral-500 underline hover:text-neutral-900">
            Export CSV
          </a>
        </div>
        {report.forecast.length === 0 ? (
          <p className="text-sm text-neutral-500">No {report.dealTerm} data yet.</p>
        ) : (
          <div className="overflow-hidden rounded-lg border border-neutral-200">
            <table className="w-full text-sm">
              <thead className="bg-neutral-50 text-left text-xs uppercase text-neutral-500">
                <tr>
                  <th className="px-4 py-2 font-medium">Category</th>
                  <th className="px-4 py-2 font-medium">{report.dealTermPlural}</th>
                  <th className="w-1/3 px-4 py-2 font-medium">Value</th>
                  <th className="px-4 py-2 font-medium">Weighted</th>
                </tr>
              </thead>
              <tbody>
                {report.forecast.map((row) => (
                  <tr key={row.forecastCategory} className="border-t border-neutral-100">
                    <td className="px-4 py-2">{FORECAST_LABELS[row.forecastCategory] ?? row.forecastCategory}</td>
                    <td className="px-4 py-2 text-neutral-600">{row.dealCount}</td>
                    <td className="px-4 py-2">
                      <div className="flex items-center gap-2">
                        <Bar value={Number(row.dealValueCents)} max={maxForecastValue} />
                        <span className="whitespace-nowrap text-xs text-neutral-500">{formatAmount(row.dealValueCents, report.defaultCurrency, report.defaultCurrency)}</span>
                      </div>
                    </td>
                    <td className="px-4 py-2 text-neutral-600">{formatAmount(row.weightedValueCents, report.defaultCurrency, report.defaultCurrency)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <section className="space-y-3">
        <div className="flex items-baseline justify-between">
          <h2 className="text-sm font-semibold text-neutral-700">Activity, last 30 days</h2>
          <a href="/api/reports/export?type=activity" className="text-xs text-neutral-500 underline hover:text-neutral-900">
            Export CSV
          </a>
        </div>
        {activityRows.length === 0 ? (
          <p className="text-sm text-neutral-500">No activity logged in the last 30 days.</p>
        ) : (
          <div className="overflow-hidden rounded-lg border border-neutral-200">
            <table className="w-full text-sm">
              <thead className="bg-neutral-50 text-left text-xs uppercase text-neutral-500">
                <tr>
                  <th className="px-4 py-2 font-medium">Type</th>
                  <th className="w-2/3 px-4 py-2 font-medium">Count</th>
                </tr>
              </thead>
              <tbody>
                {activityRows.map(([type, count]) => (
                  <tr key={type} className="border-t border-neutral-100">
                    <td className="px-4 py-2">{ACTIVITY_LABELS[type] ?? type}</td>
                    <td className="px-4 py-2">
                      <div className="flex items-center gap-2">
                        <Bar value={count} max={maxActivityCount} />
                        <span className="whitespace-nowrap text-xs text-neutral-500">{count}</span>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </div>
  );
}
