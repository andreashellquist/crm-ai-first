import type { Prisma } from "@/generated/prisma";
import { db } from "@/lib/db";

/**
 * Enqueues a job and returns immediately — the caller (a Server Action) must
 * never await the job's work itself. See backend-api-engineer: anything that
 * calls an LLM or third-party API runs off the request path.
 */
export async function enqueueJob(
  type: string,
  payload: Record<string, unknown>,
  opts?: { workspaceId?: string; maxAttempts?: number },
) {
  const job = await db.job.create({
    data: {
      type,
      payload: payload as Prisma.InputJsonValue,
      workspaceId: opts?.workspaceId,
      maxAttempts: opts?.maxAttempts ?? 3,
    },
  });
  return job.id;
}

export type JobStatus = "pending" | "processing" | "succeeded" | "failed";

/**
 * Reads a job's status, re-scoped by workspace so a caller can never read
 * another workspace's job (auth-security-expert's isolation discipline
 * applies to background-job status just as much as to any other read).
 */
export async function getJobStatus(jobId: string, workspaceId: string) {
  const job = await db.job.findFirst({
    where: { id: jobId, workspaceId },
    select: { status: true, lastError: true },
  });
  if (!job) return null;
  return { status: job.status as JobStatus, lastError: job.lastError };
}
