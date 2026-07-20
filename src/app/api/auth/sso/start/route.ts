import { NextResponse } from "next/server";
import { createApiClient } from "@/lib/api/client";

// Plain form POST target (works without JS) — the /sso page's email-entry
// form submits here. Looks up which workspace's SsoConnection claims the
// submitted email's domain (server-to-server, so the connection's Issuer/
// ClientId never reach the browser directly) and, if found, redirects the
// browser straight to the IdP's authorization endpoint. See
// backend/CrmApi/Controllers/SsoController.cs's Start action.
export async function POST(request: Request) {
  const formData = await request.formData();
  const email = formData.get("email");
  const origin = new URL(request.url).origin;

  if (typeof email !== "string" || !email.trim()) {
    return NextResponse.redirect(new URL("/sso?error=missing_email", origin));
  }

  const redirectUri = `${origin}/api/auth/callback/sso`;
  const client = createApiClient();
  const { data } = await client.POST("/api/auth/sso/start", { body: { email, redirectUri } });

  if (!data?.found || !data.authorizationUrl || !data.state) {
    return NextResponse.redirect(new URL("/sso?error=not_configured", origin));
  }

  const response = NextResponse.redirect(data.authorizationUrl);
  // Short-lived CSRF token — the callback rejects the redirect unless the
  // returned `state` matches this cookie, same convention as
  // /api/auth/google's google_oauth_state.
  response.cookies.set("sso_state", data.state, {
    httpOnly: true,
    secure: process.env.NODE_ENV === "production",
    sameSite: "lax",
    path: "/",
    maxAge: 60 * 10,
  });
  return response;
}
