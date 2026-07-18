import { test, expect } from "@playwright/test";
import { login } from "./helpers";

test("creates a contact and it appears in the table", async ({ page }) => {
  await login(page);
  await page.goto("/contacts");

  // Unique per run so this test is safe to re-run against a persisted seeded
  // database (no reset/truncate between e2e runs — same reasoning as the
  // backend's fresh-Guid-per-row convention in TestData.cs).
  const uniqueLastName = `E2E-${Date.now()}`;

  await page.getByLabel("First name").fill("Taylor");
  await page.getByLabel("Last name").fill(uniqueLastName);
  await page.getByLabel("Email", { exact: true }).fill(`taylor.${Date.now()}@example.com`);
  await page.getByLabel("Company").fill("Initech");
  await page.getByRole("button", { name: "Add contact" }).click();

  await expect(page.getByRole("row", { name: new RegExp(uniqueLastName) })).toBeVisible();
});
