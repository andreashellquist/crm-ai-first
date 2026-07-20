import { test, expect } from "@playwright/test";
import { login } from "./helpers";

// SCAFFOLD ONLY (see backend/CrmApi/Services/OidcClient.cs) — same reason
// there's no e2e test for the Google OAuth redirect either: /api/auth/sso/
// start does a real OIDC discovery call (GET {issuer}/.well-known/openid-
// configuration) against whatever Issuer a workspace configures, and there's
// no real IdP reachable from this sandbox to discover against. This is a
// reachability smoke test for what doesn't need one — configuring/removing
// a connection via /settings, and the "no connection for this domain" path.
// The full discovery -> exchange -> JIT-provisioning flow is covered
// end-to-end against a fake OIDC client by SsoControllerTests.cs in the
// backend suite.
test("configures and removes an SSO connection in settings", async ({ page }) => {
  const consoleErrors: string[] = [];
  page.on("console", (msg) => {
    if (msg.type() === "error") consoleErrors.push(msg.text());
  });
  page.on("pageerror", (err) => consoleErrors.push(String(err)));

  await login(page);
  await page.goto("/settings");

  const domain = `e2e-${Date.now()}.example`;
  const ssoSection = page.locator("section", { has: page.getByRole("heading", { name: "Single sign-on" }) });
  await ssoSection.getByLabel("Issuer (https://)").fill("https://idp.example.com");
  await ssoSection.getByLabel("Email domain").fill(domain);
  await ssoSection.getByLabel("Client ID").fill("e2e-client-id");
  await ssoSection.getByLabel("Client secret").fill("e2e-client-secret");
  await Promise.all([
    page.waitForResponse((res) => res.request().method() === "POST"),
    ssoSection.getByRole("button", { name: "Save SSO connection" }).click(),
  ]);

  await expect(ssoSection.getByText(domain)).toBeVisible();
  await expect(ssoSection.getByText("https://idp.example.com")).toBeVisible();
  await expect(ssoSection.getByText("Optional — password login still works")).toBeVisible();

  // Remove the connection so later e2e runs against this shared database
  // aren't affected. The Server Action's own DELETE call happens server-to-
  // server (Next.js -> .NET API); the browser only ever sees the action's
  // own POST, same as api-platform.spec.ts's Revoke button.
  await Promise.all([
    page.waitForResponse((res) => res.request().method() === "POST"),
    ssoSection.getByRole("button", { name: "Remove" }).click(),
  ]);
  await expect(ssoSection.getByText("No SSO connection configured.")).toBeVisible();

  expect(consoleErrors).toEqual([]);
});

test("/sso page shows an error for an email with no matching connection", async ({ page }) => {
  await page.goto("/sso");
  await page.getByLabel("Work email").fill(`nobody@${Date.now()}-unconfigured.example`);
  await page.getByRole("button", { name: "Continue" }).click();

  await expect(page).toHaveURL(/\/sso\?error=not_configured/);
  await expect(page.getByText("No single sign-on connection is set up for that email's domain.")).toBeVisible();
});
