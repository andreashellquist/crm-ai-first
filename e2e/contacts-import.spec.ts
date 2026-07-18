import { test, expect } from "@playwright/test";
import { login } from "./helpers";

test("imports a CSV, auto-maps columns, and shows a created/error summary", async ({ page }) => {
  test.setTimeout(60_000); // the import job needs a full poll cycle to land
  await login(page);
  await page.goto("/contacts");
  await page.getByRole("link", { name: "Import CSV" }).click();
  await expect(page).toHaveURL("/contacts/import");

  const uniqueEmail = `e2e.import.${Date.now()}@example.com`;
  const csv = [
    "Email,First Name,Last Name,Phone,Company",
    `${uniqueEmail},Taylor,Import,555-0177,Wonka Industries`,
    "not-an-email,Broken,Row,,",
  ].join("\n");

  await page.locator('input[type="file"]').setInputFiles({
    name: "import.csv",
    mimeType: "text/csv",
    buffer: Buffer.from(csv),
  });

  await expect(page.getByText("Map columns")).toBeVisible();
  // Auto-mapping (contacts/import/import-form.tsx's HEADER_ALIASES) should
  // have picked "Email"/"First Name"/etc. without the user touching anything.
  await expect(page.getByRole("cell", { name: uniqueEmail })).toBeVisible();

  await page.getByRole("button", { name: /^Import \d+ contacts?$/ }).click();
  await expect(page.getByText("Importing…")).toBeVisible();
  await expect(page.getByText(/\d+ created, \d+ updated, \d+ skipped/)).toBeVisible({ timeout: 45_000 });

  const summary = page.getByText(/\d+ created, \d+ updated, \d+ skipped/);
  await expect(summary).toContainText("1 created");
  // Line 3: header is line 1, the valid row is line 2, the broken row is line 3.
  await expect(page.getByText(/Row 3: Invalid email format/)).toBeVisible();

  await page.goto("/contacts");
  await expect(page.getByRole("row", { name: new RegExp(uniqueEmail) })).toBeVisible();
});
