import { test, expect } from "@playwright/test";
import { login } from "./helpers";

test("creates an API key, sees the raw key once, and can revoke it", async ({ page }) => {
  const consoleErrors: string[] = [];
  page.on("console", (msg) => {
    if (msg.type() === "error") consoleErrors.push(msg.text());
  });
  page.on("pageerror", (err) => consoleErrors.push(String(err)));

  await login(page);
  await page.goto("/settings");

  const keyName = `Zapier integration ${Date.now()}`;
  await page.getByLabel("Key name").fill(keyName);
  await page.getByRole("checkbox", { name: "contacts:read" }).check();
  await page.getByRole("button", { name: "Create key" }).click();

  await expect(page.getByText(/copy this key now/)).toBeVisible();
  const rawKey = await page.locator("code", { hasText: "crm_live_" }).textContent();
  expect(rawKey).toMatch(/^crm_live_/);

  const keyRow = page.locator("ul li", { hasText: keyName });
  await expect(keyRow).toBeVisible();

  // The issued key actually authenticates the public v1 API.
  const apiResponse = await page.request.get("http://localhost:5194/api/v1/contacts", {
    headers: { "X-Api-Key": rawKey!.trim() },
  });
  expect(apiResponse.ok()).toBeTruthy();

  await keyRow.getByRole("button", { name: "Revoke" }).click();
  await expect(page.getByText("(revoked)")).toBeVisible();

  const revokedResponse = await page.request.get("http://localhost:5194/api/v1/contacts", {
    headers: { "X-Api-Key": rawKey!.trim() },
  });
  expect(revokedResponse.status()).toBe(401);

  expect(consoleErrors).toEqual([]);
});

// Delivery mechanics (HMAC signature, retry/backoff, terminal failure) are
// covered by WebhookTests.cs in the backend suite via a fake HTTP handler —
// this is a reachability smoke test for the settings UI only, same scope as
// the AI-feature e2e tests (content assertions belong in xUnit, not here).
test("creates a webhook subscription and it shows up with an empty delivery history", async ({ page }) => {
  await login(page);
  await page.goto("/settings");

  const url = `http://127.0.0.1:${8934 + Math.floor(Math.random() * 1000)}/hook-${Date.now()}`;
  await page.getByLabel("Endpoint URL").fill(url);
  await page.getByRole("checkbox", { name: "contact.created" }).check();
  await page.getByRole("button", { name: "Add webhook" }).click();

  await expect(page.getByText(/copy this secret now/)).toBeVisible();
  const subscriptionRow = page.locator("ul li", { hasText: url });
  await expect(subscriptionRow).toBeVisible();

  await subscriptionRow.getByRole("button", { name: "View recent deliveries" }).click();
  await expect(subscriptionRow.getByText("No deliveries yet.")).toBeVisible();
});
