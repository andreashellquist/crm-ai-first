import type { Page } from "@playwright/test";

// Matches Data/Seed.cs — seeded once via `dotnet run --project backend/CrmApi
// -- seed` before the suite runs (see .github/workflows/ci.yml).
export const SEEDED_USER = { email: "demo@example.com", password: "password123" };

export async function login(page: Page) {
  await page.goto("/login");
  await page.getByLabel("Email").fill(SEEDED_USER.email);
  await page.getByLabel("Password").fill(SEEDED_USER.password);
  await page.getByRole("button", { name: "Sign in" }).click();
  // "/" immediately redirects to "/pipeline" for an authenticated session
  // (src/app/page.tsx) — wait for that landing point, not "/" itself.
  await page.waitForURL("/pipeline");
}
