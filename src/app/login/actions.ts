"use server";

import { redirect } from "next/navigation";
import { createApiClient } from "@/lib/api/client";
import { setSession } from "@/lib/session";

export async function loginAction(_prevState: string | undefined, formData: FormData) {
  const email = formData.get("email");
  const password = formData.get("password");
  if (typeof email !== "string" || typeof password !== "string") {
    return "Enter an email and password.";
  }

  const client = createApiClient();
  const { data, response } = await client.POST("/api/auth/login", {
    body: { email, password },
  });

  if (!data) {
    if (response.status === 403) return "This workspace requires single sign-on — use the SSO sign-in link instead.";
    return response.status === 401 ? "Invalid email or password." : "Sign-in failed — try again.";
  }

  await setSession({ token: data.token, workspaceId: data.workspaceId, workspaceName: data.workspaceName });
  redirect("/");
}
