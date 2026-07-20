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

// Data-subject request fulfillment — the actual anonymization/audit-log
// behavior is covered by ContactDataSubjectRequestsTests.cs; this is a
// reachability smoke test for the /contacts row actions and the export
// download route.
test("exports and erases a contact's data from the contacts table", async ({ page }) => {
  await login(page);
  await page.goto("/contacts");

  const uniqueLastName = `DSR-${Date.now()}`;
  await page.getByLabel("First name").fill("Casey");
  await page.getByLabel("Last name").fill(uniqueLastName);
  await page.getByLabel("Email", { exact: true }).fill(`casey.${Date.now()}@example.com`);
  await page.getByRole("button", { name: "Add contact" }).click();

  const row = page.getByRole("row", { name: new RegExp(uniqueLastName) });
  await expect(row).toBeVisible();

  const exportHref = await row.getByRole("link", { name: "Export" }).getAttribute("href");
  const exportResponse = await page.request.get(exportHref!);
  expect(exportResponse.ok()).toBeTruthy();
  const exported = await exportResponse.json();
  expect(exported.lastName).toBe(uniqueLastName);

  await Promise.all([
    page.waitForResponse((res) => res.request().method() === "POST"),
    row.getByRole("button", { name: /Erase/ }).click(),
  ]);
  await expect(page.getByRole("row", { name: new RegExp(uniqueLastName) })).toBeHidden();
});
