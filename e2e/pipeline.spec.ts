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
  // (then reloading) can race ahead of the real persist.
  await stageSelect.selectOption(targetValue!);
  await expect(stageSelect).toHaveValue(targetValue!); // optimistic update, immediate

  // NotificationBell polls via a Server Action too, which is
  // browser-visible as a POST to this same page URL — waiting on "any POST"
  // to know the move persisted can resolve on that unrelated poll instead
  // (the source of this test's documented flakiness). Retrying the
  // reload-and-check instead of sniffing the network directly tests the
  // thing that actually matters: eventual server-side consistency.
  await expect(async () => {
    await page.reload();
    const dealCardAfterReload = page.locator('[data-testid^="deal-card-"]', { hasText: "Globex Corporation" });
    await expect(dealCardAfterReload.getByLabel("Stage")).toHaveValue(targetValue!);
  }).toPass({ timeout: 15_000 });
});

// Previously there was no way anywhere in this app to create a Deal at all
// — only Seed.cs. The write-path coverage (company find-or-create, stage
// defaulting, custom field/contact validation) is in
// PipelineControllerTests.cs; this is a reachability smoke test for the
// pipeline board's "New deal" form.
test("creates a new deal from the pipeline board and it lands in the first stage", async ({ page }) => {
  await login(page);
  await page.goto("/pipeline");

  const companyName = `E2E Newco ${Date.now()}`;
  await page.getByLabel("Company").fill(companyName);
  await page.getByLabel(/Amount/).fill("2500");

  // NotificationBell polls via a Server Action too, which is
  // browser-visible as a POST to this same page URL — `waitForResponse`
  // keyed on "any POST" can resolve on that unrelated poll instead of this
  // form's submission. Waiting for the button's own pending state to clear
  // (same pattern as ai-features.spec.ts's AI-feature buttons) is immune to
  // that race.
  await page.getByRole("button", { name: "New deal" }).click();
  await expect(page.getByRole("button", { name: "Adding…" })).toBeHidden({ timeout: 15_000 });

  // useActionState's pending flips false as soon as createDealAction
  // returns, which can land slightly before Next.js finishes re-rendering
  // from the revalidatePath it triggered — give this its own generous
  // window rather than relying on "Adding…" hiding alone. Also covers dev
  // server JIT-compiling /pipeline on a genuinely cold first hit (this
  // spec's own first test and accessibility.spec.ts's "pipeline board" scan
  // already warm this route in a full suite run, but not when this spec
  // runs in isolation).
  await expect(page.locator('[data-testid^="deal-card-"]', { hasText: companyName })).toBeVisible({ timeout: 15_000 });
});

test("opens the deal detail page from the board", async ({ page }) => {
  await login(page);
  await page.goto("/pipeline");

  await page.getByRole("link", { name: "Globex Corporation" }).click();
  await expect(page).toHaveURL(/\/pipeline\/.+/);
  await expect(page.getByRole("heading", { name: "Globex Corporation" })).toBeVisible();
});
