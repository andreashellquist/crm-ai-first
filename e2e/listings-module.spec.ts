import { test, expect } from "@playwright/test";
import { login } from "./helpers";

// The seeded demo workspace uses the saas-sales template (no modules
// enabled) — this test turns the real-estate "listings" module on via
// /settings, uses it against the seeded deal, then turns it back off so it
// doesn't leak into other tests sharing this seeded database.
test("enabling the listings module surfaces a working Listing panel on the deal detail page", async ({ page }) => {
  const consoleErrors: string[] = [];
  page.on("console", (msg) => {
    if (msg.type() === "error") consoleErrors.push(msg.text());
  });
  page.on("pageerror", (err) => consoleErrors.push(String(err)));

  await login(page);

  await page.goto("/settings");
  const listingsCheckbox = page.getByRole("checkbox", { name: /Listings/ });
  await expect(listingsCheckbox).not.toBeChecked();

  await listingsCheckbox.check();
  await page.getByRole("button", { name: "Save modules" }).click();
  await expect(page.getByRole("button", { name: "Save modules" })).toBeEnabled();
  await page.reload();
  await expect(page.getByRole("checkbox", { name: /Listings/ })).toBeChecked();

  await page.goto("/pipeline");
  await page.getByRole("link", { name: "Globex Corporation" }).click();
  await expect(page.getByRole("heading", { name: "Listing" })).toBeVisible();

  await page.getByLabel("Listing agent").fill("Jordan Realtor");
  await page.getByLabel("Commission %").fill("2.5");
  await page.getByRole("button", { name: "Save listing" }).click();
  await expect(page.getByRole("button", { name: "Save listing" })).toBeEnabled();

  await page.reload();
  await expect(page.getByLabel("Listing agent")).toHaveValue("Jordan Realtor");
  await expect(page.getByLabel("Commission %")).toHaveValue("2.5");

  // Reset so this workspace's module state doesn't affect other tests.
  await page.goto("/settings");
  await page.getByRole("checkbox", { name: /Listings/ }).uncheck();
  await page.getByRole("button", { name: "Save modules" }).click();

  expect(consoleErrors).toEqual([]);
});
