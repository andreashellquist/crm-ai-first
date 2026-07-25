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

// Previously there was no assignee column on Deal/TaskItem at all, and no
// frontend for either — this is a reachability smoke test for both the
// AssignDealSelect and the DealTasksSection form. The write-path coverage
// (member validation, deal_assigned notification, overdue sweep) is in
// PipelineControllerTests.cs/TaskAssignmentAndOverdueSweepTests.cs.
test("assigns the seeded deal to a workspace member and it persists", async ({ page }) => {
  await login(page);
  await page.goto("/pipeline");
  await page.getByRole("link", { name: "Globex Corporation" }).click();
  await expect(page).toHaveURL(/\/pipeline\/.+/);

  const assignSelect = page.getByLabel("Assigned to");
  // Only the seeded owner is a member of this workspace — select by value
  // rather than a hardcoded name, since settings-i18n.spec.ts's profile-name
  // test permanently renames that seeded user in this shared, unreset DB.
  const memberValue = await assignSelect.locator("option").nth(1).getAttribute("value");
  expect(memberValue).toBeTruthy();
  // The Server Action call is browser-visible as a POST regardless of the
  // PUT PipelineController.AssignDeal actually does server-to-server (same
  // reasoning as pipeline.spec.ts's moveDealStageAction wait).
  await Promise.all([page.waitForResponse((res) => res.request().method() === "POST"), assignSelect.selectOption(memberValue!)]);
  const assignedValue = await assignSelect.inputValue();
  expect(assignedValue).toBe(memberValue);

  await page.reload();
  await expect(page.getByLabel("Assigned to")).toHaveValue(assignedValue);
});

// Seed.cs already associates Jane Doe with this deal, so the "existing
// association renders" case is covered by simply loading the page; this
// exercises the genuinely new capability — creating a task against a deal
// (TasksController has had full CRUD since Phase 0/1, but this is the first
// frontend anywhere in the app that ever called it).
test("shows the deal's associated contact and creates a task against it", async ({ page }) => {
  await login(page);
  await page.goto("/pipeline");
  await page.getByRole("link", { name: "Globex Corporation" }).click();
  await expect(page).toHaveURL(/\/pipeline\/.+/);

  await expect(page.getByRole("heading", { name: "Contacts" })).toBeVisible();
  await expect(page.getByRole("link", { name: "Jane Doe" })).toBeVisible();

  const taskTitle = `E2E task ${Date.now()}`;
  await page.getByLabel("New task").fill(taskTitle);
  await Promise.all([page.waitForResponse((res) => res.request().method() === "POST"), page.getByRole("button", { name: "Add task" }).click()]);

  await expect(page.getByText(taskTitle)).toBeVisible();
});
