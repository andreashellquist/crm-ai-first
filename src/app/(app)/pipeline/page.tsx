import { db } from "@/lib/db";
import { requireWorkspace } from "@/lib/workspace";
import { DealCard } from "./deal-card";

function formatAmount(cents: number | null, currency: string | null) {
  if (cents == null) return null;
  return new Intl.NumberFormat("en-US", {
    style: "currency",
    currency: currency ?? "USD",
    maximumFractionDigits: 0,
  }).format(cents / 100);
}

export default async function PipelinePage() {
  const { workspaceId } = await requireWorkspace();

  const pipeline = await db.pipeline.findFirst({
    where: { workspaceId, isDefault: true },
    include: {
      stages: {
        orderBy: { order: "asc" },
        include: {
          deals: {
            where: { deletedAt: null },
            include: { company: true },
            orderBy: { createdAt: "desc" },
          },
        },
      },
    },
  });

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
          const stageValueCents = stage.deals.reduce((sum, deal) => sum + (deal.amountCents ?? 0), 0);
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
                    title={deal.company?.name ?? "Untitled deal"}
                    amountLabel={formatAmount(deal.amountCents, deal.currency)}
                    stages={allStages}
                    currentStageId={stage.id}
                    aiScore={deal.aiScore}
                    aiScoreRationale={deal.aiScoreRationale}
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
