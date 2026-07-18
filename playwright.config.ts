import { existsSync } from "node:fs";
import { defineConfig, devices } from "@playwright/test";

// Golden-path e2e suite — see qa-test-engineer: "sign in, create a contact,
// move a deal through the pipeline, request an AI draft and see it render."
// Kept small and high-value, not exhaustive edge-case coverage (that's what
// backend/CrmApi.Tests is for). Runs sequentially against one seeded
// workspace (`dotnet run --project backend/CrmApi -- seed`), same reasoning
// as the backend's CrmApiCollection: shared mutable state, not safe under
// parallel execution.
export default defineConfig({
  testDir: "./e2e",
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [["github"], ["html", { open: "never" }]] : "list",
  use: {
    baseURL: "http://localhost:3000",
    trace: "on-first-retry",
    screenshot: "only-on-failure",
  },
  projects: [
    {
      name: "chromium",
      use: {
        ...devices["Desktop Chrome"],
        // This sandbox's pre-installed Chromium build doesn't always match
        // the revision @playwright/test's own installer would fetch —
        // pointing at it directly avoids a redundant/failing download. Local
        // dev outside this sandbox falls back to Playwright's own managed
        // browser (undefined executablePath) if this path doesn't exist.
        launchOptions: existsSync("/opt/pw-browsers/chromium") ? { executablePath: "/opt/pw-browsers/chromium" } : {},
      },
    },
  ],

  // Starts both apps against the already-seeded dev database — see the "e2e"
  // job in .github/workflows/ci.yml, or run
  // `dotnet run --project backend/CrmApi -- seed` locally first.
  // ASPNETCORE_ENVIRONMENT defaults to Development here so this also works
  // out of the box for local `pnpm test:e2e` without extra env setup —
  // matches appsettings.Development.json's ConnectionStrings, so CI's
  // Postgres service is configured with the same crm/crm/crm_dotnet_dev
  // credentials specifically so no override is needed either.
  webServer: [
    {
      command: "dotnet run --project backend/CrmApi --no-build --urls http://localhost:5194",
      url: "http://localhost:5194/health",
      reuseExistingServer: !process.env.CI,
      timeout: 60_000,
      env: { ASPNETCORE_ENVIRONMENT: process.env.ASPNETCORE_ENVIRONMENT ?? "Development" },
    },
    {
      command: "pnpm dev",
      url: "http://localhost:3000/login",
      reuseExistingServer: !process.env.CI,
      timeout: 60_000,
    },
  ],
});
