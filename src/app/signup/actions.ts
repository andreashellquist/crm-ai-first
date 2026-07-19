"use server";

import { redirect } from "next/navigation";
import { createApiClient } from "@/lib/api/client";
import { setSession } from "@/lib/session";

export async function registerAction(_prevState: string | undefined, formData: FormData) {
  const name = formData.get("name");
  const email = formData.get("email");
  const password = formData.get("password");
  const workspaceName = formData.get("workspaceName");
  const templateId = formData.get("templateId");

  if (
    typeof name !== "string" ||
    typeof email !== "string" ||
    typeof password !== "string" ||
    typeof workspaceName !== "string" ||
    typeof templateId !== "string" ||
    !name || !email || !password || !workspaceName || !templateId
  ) {
    return "Fill in every field and pick a starter template.";
  }

  const client = createApiClient();
  const { data, response } = await client.POST("/api/auth/register", {
    body: { name, email, password, workspaceName, templateId },
  });

  if (!data) {
    if (response.status === 409) return "An account with this email already exists.";
    if (response.status === 400) return await response.text();
    return "Sign-up failed — try again.";
  }

  await setSession({ token: data.token, workspaceId: data.workspaceId, workspaceName: data.workspaceName });
  redirect("/");
}
