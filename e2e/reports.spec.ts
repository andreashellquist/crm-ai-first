import { test, expect } from "@playwright/test";
import { login } from "./helpers";

test("loads the reports dashboard and refresh reaches a terminal state without console errors", async ({ page }) => {
  const consoleErrors: string[] = [];
  page.on("console", (msg) => {
    if (msg.type() === "error") consoleErrors.push(msg.text());
  });
  page.on("pageerror", (err) => consoleErrors.push(String(err)));

  await login(page);
  await page.getByRole("link", { name: "Reports" }).click();
  await expect(page).toHaveURL("/reports");

  await expect(page.getByRole("heading", { name: "Deals by stage" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Forecast by category" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Activity, last 30 days" })).toBeVisible();

  await page.getByRole("button", { name: "Refresh" }).click();
  await expect(page.getByRole("button", { name: "Refreshing…" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Refreshing…" })).toBeHidden({ timeout: 30_000 });

  expect(consoleErrors).toEqual([]);
});

test("pipeline CSV export returns a well-formed CSV with the header row", async ({ page }) => {
  await login(page);

  const response = await page.request.get("/api/reports/export?type=pipeline");
  expect(response.ok()).toBeTruthy();
  expect(response.headers()["content-type"]).toContain("text/csv");
  const csv = await response.text();
  expect(csv.split("\n")[0]).toBe("Stage,Deal count,Deal value (cents),Weighted value (cents)");
});
