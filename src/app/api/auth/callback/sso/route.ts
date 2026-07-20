import { NextResponse } from "next/server";
import { cookies } from "next/headers";
import { createApiClient } from "@/lib/api/client";
import { setSession } from "@/lib/session";

// The IdP redirects the browser here after the user authenticates there —
// same "validate CSRF state cookie, then forward the code server-to-server"
// shape as /api/auth/callback/google. redirectUri must exactly match what
// /api/auth/sso/start sent to the IdP (OIDC requirement), so it's rebuilt
// from this route's own origin rather than passed through any client state.
export async function GET(request: Request) {
  const url = new URL(request.url);
  const code = url.searchParams.get("code");
  const state = url.searchParams.get("state");
  const oauthError = url.searchParams.get("error");

  const cookieStore = await cookies();
  const expectedState = cookieStore.get("sso_state")?.value;
  cookieStore.delete("sso_state");

  if (oauthError || !code || !state || !expectedState || state !== expectedState) {
    return NextResponse.redirect(new URL("/login?error=sso_failed", url.origin));
  }

  const redirectUri = `${url.origin}/api/auth/callback/sso`;
  const client = createApiClient();
  const { data } = await client.POST("/api/auth/sso/exchange", { body: { state, code, redirectUri } });

  if (!data) {
    return NextResponse.redirect(new URL("/login?error=sso_failed", url.origin));
  }

  await setSession({ token: data.token, workspaceId: data.workspaceId, workspaceName: data.workspaceName });
  return NextResponse.redirect(new URL("/", url.origin));
}
