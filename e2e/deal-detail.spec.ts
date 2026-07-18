import { test, expect } from "@playwright/test";
import { login } from "./helpers";

test("logs an activity against the seeded deal and it appears in the list", async ({ page }) => {
  await login(page);
  await page.goto("/pipeline");
  await page.getByRole("link", { name: "Globex Corporation" }).click();
  await expect(page).toHaveURL(/\/pipeline\/.+/);

  const activityHeading = page.getByRole("heading", { name: /^Activity \(\d+\)$/ });
  const countBefore = Number((await activityHeading.textContent())?.match(/\d+/)?.[0]);

  const note = `E2E note ${Date.now()}`;
  await page.getByLabel("Log").selectOption("note");
  await page.getByPlaceholder("What happened?").fill(note);
  await page.getByRole("button", { name: "Log activity" }).click();

  await expect(page.getByText(note)).toBeVisible();
  await expect(activityHeading).toHaveText(`Activity (${countBefore + 1})`);
});
