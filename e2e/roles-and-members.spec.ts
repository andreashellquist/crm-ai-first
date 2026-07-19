import { test, expect } from "@playwright/test";
import { login } from "./helpers";

// Permission enforcement itself (a custom role granting exactly its scoped
// permissions, the last-owner guard) is covered by
// MembersControllerTests.cs/RolesControllerTests.cs in the backend suite —
// this is a reachability smoke test for the /settings UI.
test("creates a custom role and it becomes assignable to a member", async ({ page }) => {
  const consoleErrors: string[] = [];
  page.on("console", (msg) => {
    if (msg.type() === "error") consoleErrors.push(msg.text());
  });
  page.on("pageerror", (err) => consoleErrors.push(String(err)));

  await login(page);
  await page.goto("/settings");

  await expect(page.getByRole("heading", { name: "Roles" })).toBeVisible();
  await expect(page.getByText("owner —")).toBeVisible();
  await expect(page.getByText("member —")).toBeVisible();

  const roleName = `Field Editor ${Date.now()}`;
  const rolesSection = page.locator("section", { has: page.getByRole("heading", { name: "Roles" }) });
  const createRoleForm = rolesSection.locator("form");
  await createRoleForm.getByLabel("New role name").fill(roleName);
  await createRoleForm.getByRole("checkbox", { name: "fields:manage" }).check();
  await createRoleForm.getByRole("button", { name: "Create role" }).click();

  const roleRow = rolesSection.locator("li", { hasText: roleName });
  await expect(roleRow).toBeVisible();
  await expect(roleRow.getByRole("checkbox", { name: "fields:manage" })).toBeChecked();

  // The new role shows up as an assignable option on the seeded owner's
  // member row (proving Members and Roles share the same role catalog).
  const membersSection = page.locator("section", { has: page.getByRole("heading", { name: "Members" }) });
  const roleSelect = membersSection.locator("select").first();
  await expect(roleSelect.locator("option", { hasText: roleName })).toHaveCount(1);

  expect(consoleErrors).toEqual([]);
});
