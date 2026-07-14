import "dotenv/config";
import "@/lib/jobs/handlers";
import { runWorkerLoop } from "@/lib/jobs/worker";

// Local-dev / dedicated-process entrypoint: `pnpm worker`. This persistent
// loop does not run on Vercel's serverless functions — a production
// deployment there should instead expose an API route that calls
// processPendingJobs() once per invocation, triggered by Vercel Cron (see
// devops-observability-expert for the deployment story). This file is the
// dev-environment equivalent of that same worker code path.
runWorkerLoop().catch((err) => {
  console.error(err);
  process.exit(1);
});
