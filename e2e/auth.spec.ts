import { test, expect } from "@playwright/test";
import { login, SEEDED_USER } from "./helpers";

test("signs in with valid credentials and reaches the pipeline board", async ({ page }) => {
  await login(page);
  await expect(page.getByRole("heading", { name: "New Business" })).toBeVisible();
});

test("rejects invalid credentials with an inline error, no navigation", async ({ page }) => {
  await page.goto("/login");
  await page.getByLabel("Email").fill(SEEDED_USER.email);
  await page.getByLabel("Password").fill("wrong-password");
  await page.getByRole("button", { name: "Sign in" }).click();

  await expect(page.getByText(/invalid|incorrect|failed/i)).toBeVisible();
  await expect(page).toHaveURL(/\/login/);
});

test("unauthenticated visitor is redirected to login", async ({ page }) => {
  await page.goto("/pipeline");
  await expect(page).toHaveURL(/\/login/);
});
