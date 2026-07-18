import { NextResponse } from "next/server";
import { randomBytes } from "node:crypto";

// SCAFFOLD ONLY — see backend/CrmApi/Services/GoogleOAuthClient.cs. Builds
// Google's authorization URL and redirects the browser there; the Client ID
// isn't secret (it's sent to the browser here), the Client Secret never
// leaves the .NET API. This keeps the browser talking only to Google and
// this frontend, never the .NET API directly — the callback route below
// forwards the resulting code to the API server-to-server.
export async function GET(request: Request) {
  const clientId = process.env.GOOGLE_CLIENT_ID;
  if (!clientId) {
    return NextResponse.redirect(new URL("/login?error=google_not_configured", request.url));
  }

  const origin = new URL(request.url).origin;
  const redirectUri = `${origin}/api/auth/callback/google`;
  const state = randomBytes(16).toString("hex");

  const authUrl = new URL("https://accounts.google.com/o/oauth2/v2/auth");
  authUrl.searchParams.set("client_id", clientId);
  authUrl.searchParams.set("redirect_uri", redirectUri);
  authUrl.searchParams.set("response_type", "code");
  authUrl.searchParams.set("scope", "openid email profile");
  authUrl.searchParams.set("state", state);
  authUrl.searchParams.set("access_type", "online");
  authUrl.searchParams.set("prompt", "select_account");

  const response = NextResponse.redirect(authUrl);
  // Short-lived CSRF token — the callback rejects the redirect unless the
  // returned `state` matches this cookie.
  response.cookies.set("google_oauth_state", state, {
    httpOnly: true,
    secure: process.env.NODE_ENV === "production",
    sameSite: "lax",
    path: "/",
    maxAge: 60 * 10,
  });
  return response;
}
