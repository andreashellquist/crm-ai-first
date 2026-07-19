import { test, expect } from "@playwright/test";

test("signing up with the real-estate template lands in a workspace with relabeled terminology", async ({ page }) => {
  const consoleErrors: string[] = [];
  page.on("console", (msg) => {
    if (msg.type() === "error") consoleErrors.push(msg.text());
  });
  page.on("pageerror", (err) => consoleErrors.push(String(err)));

  await page.goto("/signup");

  const email = `e2e-${Date.now()}@newco.example`;
  await page.getByLabel("Your name").fill("New Owner");
  await page.getByLabel("Workspace name").fill("New Co Realty");
  await page.getByLabel("Email").fill(email);
  await page.getByLabel("Password").fill("a-strong-password");

  await page.getByRole("button", { name: /Real Estate/ }).click();
  await page.getByRole("button", { name: "Create workspace" }).click();

  await page.waitForURL("/pipeline");
  await expect(page.getByText("New Co Realty")).toBeVisible();

  // The real-estate template's stage names should render on the board —
  // confirms the chosen template actually provisioned its pipeline, not just
  // the default one.
  await expect(page.getByText("New Listing")).toBeVisible();
  await expect(page.getByText("Under Contract")).toBeVisible();

  expect(consoleErrors).toEqual([]);
});

test("signing up with a duplicate email is rejected", async ({ page }) => {
  await page.goto("/signup");
  await page.getByLabel("Your name").fill("Demo Rep");
  await page.getByLabel("Workspace name").fill("Dup Co");
  await page.getByLabel("Email").fill("demo@example.com");
  await page.getByLabel("Password").fill("a-strong-password");
  await page.getByRole("button", { name: "Create workspace" }).click();

  await expect(page.getByText(/already exists/i)).toBeVisible();
});
