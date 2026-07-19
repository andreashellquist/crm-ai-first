"use server";

import { revalidatePath } from "next/cache";
import { requireWorkspace } from "@/lib/workspace";

export type CreateContactState = {
  error?: string;
};

export async function createContactAction(
  _prevState: CreateContactState,
  formData: FormData,
): Promise<CreateContactState> {
  const { api } = await requireWorkspace();

  const firstName = formData.get("firstName");
  if (typeof firstName !== "string" || firstName.trim() === "") {
    return { error: "First name is required" };
  }

  const lastName = formData.get("lastName");
  const email = formData.get("email");
  const companyName = formData.get("companyName");

  // Validation (required fields, email format) now lives server-side in the
  // .NET API — this action just forwards the request and surfaces its error.
  const { error } = await api.POST("/api/contacts", {
    body: {
      firstName,
      lastName: typeof lastName === "string" && lastName ? lastName : null,
      email: typeof email === "string" && email ? email : null,
      companyName: typeof companyName === "string" && companyName ? companyName : null,
    },
  });

  if (error) {
    return { error: typeof error === "string" ? error : "Could not create contact" };
  }

  revalidatePath("/contacts");
  return {};
}

export type SavedView = { id: string; entityType: string; name: string; queryString: string; createdAt: string };

export async function listSavedViewsAction(entityType: string): Promise<SavedView[]> {
  const { api } = await requireWorkspace();
  const { data } = await api.GET("/api/saved-views", { params: { query: { entityType } } });
  return data ?? [];
}

export async function createSavedViewAction(entityType: string, name: string, queryString: string): Promise<void> {
  const { api } = await requireWorkspace();
  const { error } = await api.POST("/api/saved-views", { body: { entityType, name, queryString } });
  if (error) throw new Error("Could not save this view");
  revalidatePath("/contacts");
}

export async function deleteSavedViewAction(id: string): Promise<void> {
  const { api } = await requireWorkspace();
  await api.DELETE("/api/saved-views/{id}", { params: { path: { id } } });
  revalidatePath("/contacts");
}
