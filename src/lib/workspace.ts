import { redirect } from "next/navigation";
import { auth } from "@/lib/auth";
import { db } from "@/lib/db";

/**
 * Resolves the signed-in user's workspace. A user can belong to more than one
 * workspace (crm-data-model / WorkspaceMember); the walking skeleton picks
 * the first membership rather than implementing workspace switching yet.
 */
export async function requireWorkspace() {
  const session = await auth();
  if (!session?.user?.id) redirect("/login");

  const membership = await db.workspaceMember.findFirst({
    where: { userId: session.user.id },
    include: { workspace: true },
    orderBy: { createdAt: "asc" },
  });

  if (!membership) {
    throw new Error("Signed-in user has no workspace membership");
  }

  return {
    userId: session.user.id,
    workspaceId: membership.workspaceId,
    workspace: membership.workspace,
    role: membership.role,
  };
}
