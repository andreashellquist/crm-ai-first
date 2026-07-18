import "server-only";
import { redirect } from "next/navigation";
import { getSession } from "@/lib/session";
import { createApiClient } from "@/lib/api/client";

/**
 * Resolves the signed-in user's session and a typed client pre-authenticated
 * against the .NET API. Workspace selection (first membership, no switcher
 * UI yet) happens once server-side at login — see AuthController.
 */
export async function requireWorkspace() {
  const session = await getSession();
  if (!session) redirect("/login");

  return {
    workspaceId: session.workspaceId,
    workspaceName: session.workspaceName,
    api: createApiClient(session.token),
  };
}
