import { test, expect } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { login } from "./helpers";

// WCAG 2.1 AA audit across core flows (docs/PRODUCT_SCOPE.md: "accessibility
// is ongoing from Phase 0, audited at Phase 3"). axe-core catches the
// mechanically-detectable subset of WCAG (labels, roles, contrast, landmarks)
// — it's not a substitute for manual keyboard/screen-reader testing, but it's
// a real regression guard against the most common violations creeping back
// in as pages change. wcag2a/wcag2aa/wcag21a/wcag21aa are axe's standard tag
// set for "WCAG 2.1 AA."
const WCAG_TAGS = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"];

async function auditPage(page: import("@playwright/test").Page) {
  const results = await new AxeBuilder({ page }).withTags(WCAG_TAGS).analyze();
  expect(results.violations, JSON.stringify(results.violations, null, 2)).toEqual([]);
}

test("login page has no WCAG 2.1 AA violations", async ({ page }) => {
  await page.goto("/login");
  await auditPage(page);
});

test("signup page has no WCAG 2.1 AA violations", async ({ page }) => {
  await page.goto("/signup");
  await auditPage(page);
});

test("pipeline board has no WCAG 2.1 AA violations", async ({ page }) => {
  await login(page);
  await auditPage(page);
});

test("deal detail page has no WCAG 2.1 AA violations", async ({ page }) => {
  await login(page);
  await page.getByRole("link", { name: "Globex Corporation" }).click();
  await expect(page).toHaveURL(/\/pipeline\/.+/);
  await auditPage(page);
});

test("contacts list has no WCAG 2.1 AA violations", async ({ page }) => {
  await login(page);
  await page.goto("/contacts");
  await auditPage(page);
});

test("contacts import page has no WCAG 2.1 AA violations", async ({ page }) => {
  await login(page);
  await page.goto("/contacts/import");
  await auditPage(page);
});

test("reports dashboard has no WCAG 2.1 AA violations", async ({ page }) => {
  await login(page);
  await page.goto("/reports");
  await auditPage(page);
});

test("settings page has no WCAG 2.1 AA violations", async ({ page }) => {
  await login(page);
  await page.goto("/settings");
  await auditPage(page);
});

test("ask AI page has no WCAG 2.1 AA violations", async ({ page }) => {
  await login(page);
  await page.goto("/ask");
  await auditPage(page);
});
