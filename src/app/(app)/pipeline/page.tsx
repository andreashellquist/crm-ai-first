import { requireWorkspace } from "@/lib/workspace";
import { PipelineBoard } from "./pipeline-board";

export default async function PipelinePage() {
  const { api } = await requireWorkspace();
  const { data: pipeline } = await api.GET("/api/pipeline");

  if (!pipeline) {
    return <p className="text-sm text-neutral-500">No pipeline configured for this workspace yet.</p>;
  }

  return (
    <div className="space-y-4">
      <div>
        <h1 className="text-xl font-semibold">{pipeline.name}</h1>
        <p className="text-sm text-neutral-500">
          {pipeline.stages.reduce((sum, stage) => sum + stage.deals.length, 0)} open deals
        </p>
      </div>

      <PipelineBoard pipeline={pipeline} />
    </div>
  );
}
