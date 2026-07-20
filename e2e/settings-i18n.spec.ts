import { test, expect } from "@playwright/test";
import { login } from "./helpers";

// Reachability smoke test for the i18n-currency-timezone wiring — the real
// validation/persistence logic (currency-code format, IANA timezone
// validation, per-user isolation) is covered by WorkspaceSettingsControllerTests
// and MeControllerTests in the backend suite.
test("updates default currency and profile timezone, and both surface elsewhere in the app", async ({ page }) => {
  const consoleErrors: string[] = [];
  page.on("console", (msg) => {
    if (msg.type() === "error") consoleErrors.push(msg.text());
  });
  page.on("pageerror", (err) => consoleErrors.push(String(err)));

  await login(page);
  await page.goto("/settings");

  const profileSection = page.locator("section", { has: page.getByRole("heading", { name: "My profile" }) });
  await profileSection.getByLabel("Name").fill("E2E Tester");
  await profileSection.getByLabel("Timezone (IANA)").fill("America/New_York");
  await Promise.all([
    page.waitForResponse((res) => res.request().method() === "POST"),
    profileSection.getByRole("button", { name: "Save profile" }).click(),
  ]);
  await expect(profileSection.getByLabel("Name")).toHaveValue("E2E Tester");

  const currencySection = page.locator("section", { has: page.getByRole("heading", { name: "Default currency" }) });
  await currencySection.getByLabel("Default currency").fill("EUR");
  await Promise.all([
    page.waitForResponse((res) => res.request().method() === "POST"),
    currencySection.getByRole("button", { name: "Save currency" }).click(),
  ]);
  await expect(currencySection.getByLabel("Default currency")).toHaveValue("EUR");

  // Currency change surfaces on the pipeline board's stage totals.
  await page.getByRole("link", { name: "Pipeline" }).click();
  await expect(page).toHaveURL("/pipeline");
  await expect(page.getByText("€", { exact: false }).first()).toBeVisible();

  // Timezone change surfaces on the reports page's refreshed-at display.
  await page.getByRole("link", { name: "Reports" }).click();
  await expect(page).toHaveURL("/reports");
  await page.getByRole("button", { name: "Refresh" }).click();
  await expect(page.getByRole("button", { name: "Refreshing…" })).toBeHidden({ timeout: 30_000 });
  await expect(page.getByText("America/New_York")).toBeVisible();

  // Reset workspace-level state so later test runs aren't affected.
  await page.goto("/settings");
  const currencySectionAgain = page.locator("section", { has: page.getByRole("heading", { name: "Default currency" }) });
  await currencySectionAgain.getByLabel("Default currency").fill("USD");
  await Promise.all([
    page.waitForResponse((res) => res.request().method() === "POST"),
    currencySectionAgain.getByRole("button", { name: "Save currency" }).click(),
  ]);

  expect(consoleErrors).toEqual([]);
});
