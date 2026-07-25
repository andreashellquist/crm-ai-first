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

// Contact/company detail pages previously didn't exist at all — global
// search results for either had nowhere to link to but the contacts list.
// This is a reachability smoke test for both new pages and the link between
// them; the DTO shape (activities/deals scoping, no audit log on routine
// view) is covered by ContactsControllerTests.cs/CompaniesControllerTests.cs.
test("opens a contact's detail page from the contacts list and navigates to its company", async ({ page }) => {
  await login(page);
  await page.goto("/contacts");

  await page.getByRole("link", { name: "Jane Doe" }).click();
  await expect(page).toHaveURL(/\/contacts\/.+/);
  await expect(page.getByRole("heading", { name: "Jane Doe" })).toBeVisible();
  await expect(page.getByRole("heading", { name: /^Deals \(\d+\)$/ })).toBeVisible();
  await expect(page.getByRole("heading", { name: /^Activity \(\d+\)$/ })).toBeVisible();

  // "Globex Corporation" also appears as a deal link further down the page
  // (Deal has no free-text title, so its link text is its company's name
  // too) — the header's company link is the first one in DOM order.
  await page.getByRole("link", { name: "Globex Corporation" }).first().click();
  await expect(page).toHaveURL(/\/companies\/.+/);
  await expect(page.getByRole("heading", { name: "Globex Corporation" })).toBeVisible();
  await expect(page.getByRole("link", { name: "Jane Doe" })).toBeVisible();
});

test("global search results for a contact and a company link to their detail pages", async ({ page }) => {
  await login(page);
  // /reports has no contact/company/deal links of its own to collide with
  // the search dropdown's results (unlike /pipeline, whose deal cards would
  // also match "Globex Corporation" by name).
  await page.goto("/reports");

  const searchBox = page.getByPlaceholder("Search contacts, companies, deals…");

  // "Jane" matches the Contact by name only (Company/Deal search matches by
  // company name/domain, not contact name), so this result is unambiguous.
  await searchBox.fill("Jane");
  const contactResult = page.getByRole("link", { name: /^Jane Doe/ });
  await expect(contactResult).toBeVisible();
  await expect(contactResult).toHaveAttribute("href", /^\/contacts\//);

  // "globex.example" matches the Company by domain only, not any Contact or
  // Deal (Deal search matches by company *name*, not domain).
  await searchBox.fill("globex.example");
  const companyResult = page.getByRole("link", { name: /^Globex Corporation/ });
  await expect(companyResult).toBeVisible();
  await expect(companyResult).toHaveAttribute("href", /^\/companies\//);
});
