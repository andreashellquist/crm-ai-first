import { requireWorkspace } from "@/lib/workspace";
import { formatAmount, sumCents } from "@/lib/money";
import { DealCard } from "./deal-card";

export default async function PipelinePage() {
  const { api } = await requireWorkspace();
  const { data: pipeline } = await api.GET("/api/pipeline");

  if (!pipeline) {
    return <p className="text-sm text-neutral-500">No pipeline configured for this workspace yet.</p>;
  }

  const allStages = pipeline.stages.map((stage) => ({ id: stage.id, name: stage.name }));

  return (
    <div className="space-y-4">
      <div>
        <h1 className="text-xl font-semibold">{pipeline.name}</h1>
        <p className="text-sm text-neutral-500">
          {pipeline.stages.reduce((sum, stage) => sum + stage.deals.length, 0)} open deals
        </p>
      </div>

      <div className="flex gap-4 overflow-x-auto pb-4">
        {pipeline.stages.map((stage) => {
          const stageValueCents = sumCents(stage.deals.map((deal) => deal.amountCents));
          return (
            <div key={stage.id} className="w-64 flex-none rounded-lg bg-neutral-50 p-3">
              <div className="mb-3 flex items-baseline justify-between">
                <h2 className="text-sm font-semibold">{stage.name}</h2>
                <span className="text-xs text-neutral-500">{stage.deals.length}</span>
              </div>
              <div className="mb-3 text-xs text-neutral-500">
                {formatAmount(stageValueCents, "USD")}
              </div>
              <div className="space-y-2">
                {stage.deals.map((deal) => (
                  <DealCard
                    key={deal.id}
                    dealId={deal.id}
                    title={deal.title}
                    amountLabel={formatAmount(deal.amountCents, deal.currency)}
                    stages={allStages}
                    currentStageId={stage.id}
                    aiScore={deal.aiScore == null ? null : Number(deal.aiScore)}
                    aiScoreRationale={deal.aiScoreRationale ?? null}
                  />
                ))}
                {stage.deals.length === 0 ? (
                  <p className="text-xs text-neutral-400">No deals</p>
                ) : null}
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}
