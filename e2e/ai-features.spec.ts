import { test, expect, type Page } from "@playwright/test";
import { login } from "./helpers";

// These smoke-test that each AI feature's request/poll/render loop reaches a
// terminal UI state cleanly — they do NOT assert on generated content. CI has
// no ANTHROPIC_API_KEY configured (see appsettings.Development.json), so the
// job queue's real call fails and these features surface a "temporarily
// unavailable" error — that is the *expected* terminal state here, not a
// deterministic content check (that belongs in backend/CrmApi.Tests, which
// injects FakeAnthropicMessagesClient — see qa-test-engineer). If a real key
// is ever wired into this environment, the success path renders instead and
// these assertions (button leaves its loading state, no console errors)
// still hold either way.
function trackConsoleErrors(page: Page) {
  const errors: string[] = [];
  page.on("console", (msg) => {
    if (msg.type() === "error") errors.push(msg.text());
  });
  page.on("pageerror", (err) => errors.push(String(err)));
  return errors;
}

test("deal scoring reaches a terminal state without console errors", async ({ page }) => {
  const consoleErrors = trackConsoleErrors(page);
  await login(page);
  await page.goto("/pipeline");

  const dealCard = page.locator('[data-testid^="deal-card-"]', { hasText: "Globex Corporation" });
  await dealCard.getByRole("button", { name: /score with ai/i }).click();
  await expect(dealCard.getByRole("button", { name: "Scoring…" })).toBeVisible();
  await expect(dealCard.getByRole("button", { name: "Scoring…" })).toBeHidden({ timeout: 45_000 });

  expect(consoleErrors).toEqual([]);
});

test("deal summarization reaches a terminal state without console errors", async ({ page }) => {
  const consoleErrors = trackConsoleErrors(page);
  await login(page);
  await page.goto("/pipeline");
  await page.getByRole("link", { name: "Globex Corporation" }).click();

  await page.getByRole("button", { name: /summarize with ai|re-summarize/i }).click();
  await expect(page.getByRole("button", { name: "Summarizing…" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Summarizing…" })).toBeHidden({ timeout: 45_000 });

  expect(consoleErrors).toEqual([]);
});

test("email drafting reaches a terminal state without console errors", async ({ page }) => {
  const consoleErrors = trackConsoleErrors(page);
  await login(page);
  await page.goto("/pipeline");
  await page.getByRole("link", { name: "Globex Corporation" }).click();

  await page.getByRole("button", { name: /draft with ai/i }).click();
  await expect(page.getByRole("button", { name: "Drafting…" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Drafting…" })).toBeHidden({ timeout: 45_000 });

  expect(consoleErrors).toEqual([]);
});

test("next-best-action reaches a terminal state without console errors", async ({ page }) => {
  const consoleErrors = trackConsoleErrors(page);
  await login(page);
  await page.goto("/pipeline");
  await page.getByRole("link", { name: "Globex Corporation" }).click();

  await page.getByRole("button", { name: /suggest with ai/i }).click();
  await expect(page.getByRole("button", { name: "Thinking…" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Thinking…" })).toBeHidden({ timeout: 45_000 });

  expect(consoleErrors).toEqual([]);
});

test("ask AI reaches a terminal state without console errors", async ({ page }) => {
  const consoleErrors = trackConsoleErrors(page);
  await login(page);
  await page.getByRole("link", { name: "Ask AI" }).click();
  await expect(page).toHaveURL("/ask");

  await page.getByPlaceholder("Ask a question about a deal, contact, or company…").fill("what have we discussed with Globex about pricing");
  await page.getByRole("button", { name: "Ask" }).click();
  await expect(page.getByRole("button", { name: "Thinking…" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Thinking…" })).toBeHidden({ timeout: 45_000 });

  expect(consoleErrors).toEqual([]);
});
