import { db } from "@/lib/db";

type JobHandler = (payload: Record<string, unknown>, ctx: { workspaceId: string | null }) => Promise<void>;

const handlers = new Map<string, JobHandler>();

export function registerHandler(type: string, handler: JobHandler) {
  handlers.set(type, handler);
}

interface ClaimedJob {
  id: string;
  workspaceId: string | null;
  type: string;
  payload: Record<string, unknown>;
  attempts: number;
  maxAttempts: number;
}

/**
 * Atomically claims up to `batchSize` due jobs via FOR UPDATE SKIP LOCKED so
 * multiple worker processes never double-process the same row — a single
 * UPDATE...RETURNING statement, no separate transaction needed since it's
 * already one atomic statement.
 */
async function claimJobs(batchSize: number): Promise<ClaimedJob[]> {
  return db.$queryRaw<ClaimedJob[]>`
    UPDATE "Job"
    SET status = 'processing', "lockedAt" = now(), attempts = attempts + 1, "updatedAt" = now()
    WHERE id IN (
      SELECT id FROM "Job"
      WHERE status = 'pending' AND "runAt" <= now()
      ORDER BY "runAt" ASC
      LIMIT ${batchSize}
      FOR UPDATE SKIP LOCKED
    )
    RETURNING id, "workspaceId", type, payload, attempts, "maxAttempts"
  `;
}

function backoffMs(attempts: number) {
  // 2s, 4s, 8s, ... capped at 60s
  return Math.min(2000 * 2 ** attempts, 60_000);
}

/**
 * Claims and runs one batch of due jobs, then returns — this is the unit
 * `runWorkerLoop` calls repeatedly for local dev, and the same function a
 * Vercel Cron-triggered API route would call per invocation in production
 * (a persistent loop like this file's `runWorkerLoop` doesn't run on
 * serverless; see the note in package.json's `worker` script / README).
 */
export async function processPendingJobs(batchSize = 5) {
  const jobs = await claimJobs(batchSize);
  let succeeded = 0;
  let failed = 0;

  for (const job of jobs) {
    const handler = handlers.get(job.type);
    const startedAt = Date.now();

    if (!handler) {
      await db.job.update({
        where: { id: job.id },
        data: { status: "failed", lastError: `No handler registered for type "${job.type}"` },
      });
      failed++;
      continue;
    }

    try {
      await handler(job.payload, { workspaceId: job.workspaceId });
      await db.job.update({ where: { id: job.id }, data: { status: "succeeded" } });
      succeeded++;
      console.log(
        JSON.stringify({
          event: "job_processed",
          jobId: job.id,
          type: job.type,
          outcome: "succeeded",
          durationMs: Date.now() - startedAt,
        }),
      );
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      const willRetry = job.attempts < job.maxAttempts;
      await db.job.update({
        where: { id: job.id },
        data: willRetry
          ? { status: "pending", runAt: new Date(Date.now() + backoffMs(job.attempts)), lastError: message }
          : { status: "failed", lastError: message },
      });
      failed++;
      console.error(
        JSON.stringify({
          event: "job_processed",
          jobId: job.id,
          type: job.type,
          outcome: willRetry ? "retrying" : "failed",
          attempts: job.attempts,
          durationMs: Date.now() - startedAt,
          error: message,
        }),
      );
    }
  }

  return { claimed: jobs.length, succeeded, failed };
}

export async function runWorkerLoop(pollIntervalMs = 2000) {
  console.log(JSON.stringify({ event: "worker_started", pollIntervalMs }));
  let shuttingDown = false;
  const shutdown = () => {
    shuttingDown = true;
  };
  process.once("SIGINT", shutdown);
  process.once("SIGTERM", shutdown);

  while (!shuttingDown) {
    const result = await processPendingJobs();
    if (result.claimed === 0) {
      await new Promise((resolve) => setTimeout(resolve, pollIntervalMs));
    }
  }
  console.log(JSON.stringify({ event: "worker_stopped" }));
}
