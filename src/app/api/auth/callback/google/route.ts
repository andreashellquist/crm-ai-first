import { NextResponse } from "next/server";
import { cookies } from "next/headers";
import { createApiClient } from "@/lib/api/client";
import { setSession } from "@/lib/session";

// SCAFFOLD ONLY. Google redirects the browser here after consent; this
// route validates the CSRF state cookie set by /api/auth/google, then
// forwards the authorization code to the .NET API server-to-server
// (POST /api/auth/google/exchange) — the API does the actual token exchange
// with Google and issues our own JWT, same as password login. The browser
// never talks to the .NET API directly.
export async function GET(request: Request) {
  const url = new URL(request.url);
  const code = url.searchParams.get("code");
  const state = url.searchParams.get("state");
  const oauthError = url.searchParams.get("error");

  const cookieStore = await cookies();
  const expectedState = cookieStore.get("google_oauth_state")?.value;
  cookieStore.delete("google_oauth_state");

  if (oauthError || !code || !state || !expectedState || state !== expectedState) {
    return NextResponse.redirect(new URL("/login?error=oauth_failed", url.origin));
  }

  const client = createApiClient();
  const { data } = await client.POST("/api/auth/google/exchange", { body: { code } });

  if (!data) {
    return NextResponse.redirect(new URL("/login?error=oauth_failed", url.origin));
  }

  await setSession({ token: data.token, workspaceId: data.workspaceId, workspaceName: data.workspaceName });
  return NextResponse.redirect(new URL("/", url.origin));
}
