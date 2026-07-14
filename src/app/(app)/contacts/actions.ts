"use server";

import { z } from "zod";
import { revalidatePath } from "next/cache";
import { db } from "@/lib/db";
import { requireWorkspace } from "@/lib/workspace";

const createContactSchema = z.object({
  firstName: z.string().min(1, "First name is required"),
  lastName: z.string().optional(),
  email: z.string().email("Enter a valid email").optional().or(z.literal("")),
  companyName: z.string().optional(),
});

export type CreateContactState = {
  error?: string;
};

export async function createContactAction(
  _prevState: CreateContactState,
  formData: FormData,
): Promise<CreateContactState> {
  const { workspaceId } = await requireWorkspace();

  const parsed = createContactSchema.safeParse({
    firstName: formData.get("firstName"),
    lastName: formData.get("lastName"),
    email: formData.get("email"),
    companyName: formData.get("companyName"),
  });

  if (!parsed.success) {
    return { error: parsed.error.issues[0]?.message ?? "Invalid input" };
  }

  const { firstName, lastName, email, companyName } = parsed.data;

  let companyId: string | undefined;
  if (companyName) {
    const company = await db.company.findFirst({
      where: { workspaceId, name: companyName },
    });
    companyId =
      company?.id ??
      (await db.company.create({ data: { workspaceId, name: companyName } })).id;
  }

  await db.contact.create({
    data: {
      workspaceId,
      firstName,
      lastName: lastName || undefined,
      email: email || undefined,
      companyId,
    },
  });

  revalidatePath("/contacts");
  return {};
}
