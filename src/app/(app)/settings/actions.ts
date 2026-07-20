"use server";

import { revalidatePath } from "next/cache";
import { requireWorkspace } from "@/lib/workspace";
import type { components } from "@/lib/api/schema";

export async function updateModulesAction(_prevState: string | undefined, formData: FormData) {
  const { api } = await requireWorkspace();
  const enabledModules = formData.getAll("modules").map(String);

  const { error } = await api.PUT("/api/workspace/settings", {
    body: { terminology: null, enabledModules },
  });

  if (error) return "You need to be an owner or admin to change workspace settings.";
  return undefined;
}

export async function updateCurrencyAction(_prevState: string | undefined, formData: FormData) {
  const { api } = await requireWorkspace();
  const defaultCurrency = String(formData.get("defaultCurrency") ?? "").toUpperCase();

  const { error } = await api.PUT("/api/workspace/settings", {
    body: { terminology: null, enabledModules: null, defaultCurrency },
  });

  if (error) return "Enter a 3-letter currency code (e.g. USD) — you also need to be an owner or admin.";
  revalidatePath("/settings");
  return undefined;
}

export async function updateProfileAction(_prevState: string | undefined, formData: FormData) {
  const { api } = await requireWorkspace();
  const name = formData.get("name");
  const timezone = formData.get("timezone");

  const { error } = await api.PUT("/api/me", {
    body: { name: typeof name === "string" && name.trim() ? name : null, timezone: String(timezone ?? "UTC") },
  });

  if (error) return "Enter a valid IANA timezone (e.g. America/New_York).";
  revalidatePath("/settings");
  return undefined;
}

export type UpdateSsoState = { error?: string };

export async function updateSsoAction(_prevState: UpdateSsoState, formData: FormData): Promise<UpdateSsoState> {
  const { api } = await requireWorkspace();
  const issuer = String(formData.get("issuer") ?? "");
  const clientId = String(formData.get("clientId") ?? "");
  const clientSecret = String(formData.get("clientSecret") ?? "");
  const emailDomain = String(formData.get("emailDomain") ?? "");
  const enforced = formData.get("enforced") === "on";

  const { error } = await api.PUT("/api/workspace/sso", {
    body: { issuer, clientId, clientSecret, emailDomain, enforced, isActive: true },
  });

  if (error) return { error: "Could not save the SSO connection — check the issuer/domain, or you may need to be an owner or admin." };
  revalidatePath("/settings");
  return {};
}

export async function deleteSsoAction() {
  const { api } = await requireWorkspace();
  await api.DELETE("/api/workspace/sso");
  revalidatePath("/settings");
}

export type CreateApiKeyState = { error?: string; rawKey?: string; keyName?: string };

export async function createApiKeyAction(_prevState: CreateApiKeyState, formData: FormData): Promise<CreateApiKeyState> {
  const { api } = await requireWorkspace();
  const name = formData.get("name");
  const scopes = formData.getAll("scopes").map(String);
  if (typeof name !== "string" || !name.trim()) return { error: "Name is required" };
  if (scopes.length === 0) return { error: "Pick at least one scope" };

  const { data, error } = await api.POST("/api/api-keys", { body: { name, scopes } });
  if (error || !data) {
    return { error: "Could not create the key — you may need to be an owner or admin." };
  }

  revalidatePath("/settings");
  return { rawKey: data.rawKey, keyName: data.key.name };
}

export async function revokeApiKeyAction(id: string) {
  const { api } = await requireWorkspace();
  await api.DELETE("/api/api-keys/{id}", { params: { path: { id } } });
  revalidatePath("/settings");
}

export type CreateWebhookState = { error?: string; secret?: string; url?: string };

export async function createWebhookAction(_prevState: CreateWebhookState, formData: FormData): Promise<CreateWebhookState> {
  const { api } = await requireWorkspace();
  const url = formData.get("url");
  const eventTypes = formData.getAll("eventTypes").map(String);
  if (typeof url !== "string" || !url.trim()) return { error: "URL is required" };
  if (eventTypes.length === 0) return { error: "Pick at least one event" };

  const { data, error } = await api.POST("/api/webhook-subscriptions", { body: { url, eventTypes } });
  if (error || !data) {
    return { error: "Could not create the webhook — check the URL/events, or you may need to be an owner or admin." };
  }

  revalidatePath("/settings");
  return { secret: data.secret, url: data.subscription.url };
}

export async function toggleWebhookAction(id: string, eventTypes: string[], isActive: boolean) {
  const { api } = await requireWorkspace();
  await api.PUT("/api/webhook-subscriptions/{id}", { params: { path: { id } }, body: { eventTypes, isActive } });
  revalidatePath("/settings");
}

export async function deleteWebhookAction(id: string) {
  const { api } = await requireWorkspace();
  await api.DELETE("/api/webhook-subscriptions/{id}", { params: { path: { id } } });
  revalidatePath("/settings");
}

export async function regenerateWebhookSecretAction(id: string): Promise<string | null> {
  const { api } = await requireWorkspace();
  const { data } = await api.POST("/api/webhook-subscriptions/{id}/regenerate-secret", { params: { path: { id } } });
  return data?.secret ?? null;
}

export type WebhookDelivery = components["schemas"]["WebhookDeliveryDto"];

export async function fetchWebhookDeliveriesAction(id: string): Promise<WebhookDelivery[]> {
  const { api } = await requireWorkspace();
  const { data } = await api.GET("/api/webhook-subscriptions/{id}/deliveries", { params: { path: { id } } });
  return data ?? [];
}

export type CreateRoleState = { error?: string };

export async function createRoleAction(_prevState: CreateRoleState, formData: FormData): Promise<CreateRoleState> {
  const { api } = await requireWorkspace();
  const name = formData.get("name");
  const permissions = formData.getAll("permissions").map(String);
  if (typeof name !== "string" || !name.trim()) return { error: "Name is required" };

  const { error } = await api.POST("/api/roles", { body: { name, permissions } });
  if (error) return { error: "Could not create the role — the name may already be taken, or you may need to be an owner." };

  revalidatePath("/settings");
  return {};
}

export async function updateRolePermissionsAction(id: string, permissions: string[]) {
  const { api } = await requireWorkspace();
  await api.PUT("/api/roles/{id}", { params: { path: { id } }, body: { permissions } });
  revalidatePath("/settings");
}

export async function deleteRoleAction(id: string) {
  const { api } = await requireWorkspace();
  await api.DELETE("/api/roles/{id}", { params: { path: { id } } });
  revalidatePath("/settings");
}

export async function updateMemberRoleAction(id: string, role: string) {
  const { api } = await requireWorkspace();
  await api.PUT("/api/members/{id}/role", { params: { path: { id } }, body: { role } });
  revalidatePath("/settings");
}

export async function removeMemberAction(id: string) {
  const { api } = await requireWorkspace();
  await api.DELETE("/api/members/{id}", { params: { path: { id } } });
  revalidatePath("/settings");
}
