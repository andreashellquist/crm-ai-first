import "server-only";
import { cookies } from "next/headers";

const COOKIE_NAME = "crm_session";

interface SessionData {
  token: string;
  workspaceId: string;
  workspaceName: string;
}

// Plain HttpOnly-cookie session holding the JWT the .NET API issued — this
// replaces NextAuth (which was pinned to a beta v5 release; see
// auth-security-expert). No client JS ever sees the token.
export async function setSession(session: SessionData) {
  (await cookies()).set(COOKIE_NAME, JSON.stringify(session), {
    httpOnly: true,
    secure: process.env.NODE_ENV === "production",
    sameSite: "lax",
    path: "/",
    maxAge: 60 * 60 * 24 * 7, // matches the JWT's 7-day expiry
  });
}

export async function clearSession() {
  (await cookies()).delete(COOKIE_NAME);
}

export async function getSession(): Promise<SessionData | null> {
  const raw = (await cookies()).get(COOKIE_NAME)?.value;
  if (!raw) return null;
  try {
    return JSON.parse(raw) as SessionData;
  } catch {
    return null;
  }
}
