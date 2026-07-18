import { test, expect } from "@playwright/test";
import { login } from "./helpers";

// Uses the "Stage" <select> fallback rather than simulating native HTML5
// drag-and-drop — same underlying handleMove() as the drag path (see
// pipeline-board.tsx), and far more reliable to drive from a test than
// synthesizing dragstart/dragover/drop events.
test("moves the seeded deal to a new stage via the stage selector and it persists", async ({ page }) => {
  await login(page);
  await page.goto("/pipeline");

  const dealCard = page.locator('[data-testid^="deal-card-"]', { hasText: "Globex Corporation" });
  await expect(dealCard).toBeVisible();

  const stageSelect = dealCard.getByLabel("Stage");
  const currentStage = await stageSelect.inputValue();
  const optionValues = await stageSelect
    .locator("option")
    .evaluateAll((opts) => opts.map((o) => (o as HTMLOptionElement).value));
  const targetValue = optionValues.find((v) => v !== currentStage);
  expect(targetValue).toBeTruthy();

  // onMove calls handleMove via `void handleMove(...)` (pipeline-board.tsx)
  // — the optimistic UI update lands before the moveDealStageAction Server
  // Action's POST actually resolves, so asserting the <select> value alone
  // (then reloading) can race ahead of the real persist. Wait for the POST
  // itself so the reload below reflects what the server actually committed.
  await Promise.all([
    page.waitForResponse((res) => res.request().method() === "POST"),
    stageSelect.selectOption(targetValue!),
  ]);
  await expect(stageSelect).toHaveValue(targetValue!);

  await page.reload();
  const dealCardAfterReload = page.locator('[data-testid^="deal-card-"]', { hasText: "Globex Corporation" });
  await expect(dealCardAfterReload.getByLabel("Stage")).toHaveValue(targetValue!);
});

test("opens the deal detail page from the board", async ({ page }) => {
  await login(page);
  await page.goto("/pipeline");

  await page.getByRole("link", { name: "Globex Corporation" }).click();
  await expect(page).toHaveURL(/\/pipeline\/.+/);
  await expect(page.getByRole("heading", { name: "Globex Corporation" })).toBeVisible();
});
