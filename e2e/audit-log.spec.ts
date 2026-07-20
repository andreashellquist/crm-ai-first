import { test, expect } from "@playwright/test";
import { login } from "./helpers";

// Reachability smoke test — the write-path coverage (every wired controller
// action producing exactly the right AuditLog row, workspace isolation) is
// in AuditLogTests.cs in the backend suite.
test("creating a role logs an audit entry visible in the audit log", async ({ page }) => {
  const consoleErrors: string[] = [];
  page.on("console", (msg) => {
    if (msg.type() === "error") consoleErrors.push(msg.text());
  });
  page.on("pageerror", (err) => consoleErrors.push(String(err)));

  await login(page);
  await page.goto("/settings");

  await expect(page.getByRole("heading", { name: "Audit log" })).toBeVisible();

  const roleName = `Audit Test Role ${Date.now()}`;
  const rolesSection = page.locator("section", { has: page.getByRole("heading", { name: "Roles" }) });
  const createRoleForm = rolesSection.locator("form");
  await createRoleForm.getByLabel("New role name").fill(roleName);
  await createRoleForm.getByRole("checkbox", { name: "webhooks:manage" }).check();
  await Promise.all([
    page.waitForResponse((res) => res.request().method() === "POST"),
    createRoleForm.getByRole("button", { name: "Create role" }).click(),
  ]);

  const auditSection = page.locator("section", { has: page.getByRole("heading", { name: "Audit log" }) });
  await expect(auditSection.getByRole("row").filter({ hasText: "Created a role" }).first()).toBeVisible();

  expect(consoleErrors).toEqual([]);
});
