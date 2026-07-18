"use client";

import { useActionState } from "react";
import { useSearchParams } from "next/navigation";
import { loginAction } from "./actions";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";

const OAUTH_ERROR_MESSAGES: Record<string, string> = {
  oauth_failed: "Google sign-in failed — try again.",
  google_not_configured: "Google sign-in isn't set up for this environment yet.",
};

export function LoginForm() {
  const [error, formAction, pending] = useActionState(loginAction, undefined);
  const searchParams = useSearchParams();
  const oauthError = searchParams.get("error");

  return (
    <form action={formAction} className="w-full max-w-sm space-y-4 rounded-lg border border-neutral-200 p-6">
      <div className="space-y-1">
        <h1 className="text-lg font-semibold">Sign in</h1>
        <p className="text-sm text-neutral-500">
          Dev credentials: demo@example.com / password123
        </p>
      </div>

      <div className="space-y-1">
        <label htmlFor="email" className="text-sm font-medium">
          Email
        </label>
        <Input id="email" name="email" type="email" required autoComplete="email" />
      </div>

      <div className="space-y-1">
        <label htmlFor="password" className="text-sm font-medium">
          Password
        </label>
        <Input id="password" name="password" type="password" required autoComplete="current-password" />
      </div>

      {error ? <p className="text-sm text-red-600">{error}</p> : null}
      {oauthError ? (
        <p className="text-sm text-red-600">{OAUTH_ERROR_MESSAGES[oauthError] ?? "Sign-in failed — try again."}</p>
      ) : null}

      <Button type="submit" className="w-full" disabled={pending}>
        {pending ? "Signing in…" : "Sign in"}
      </Button>

      <div className="flex items-center gap-2 text-xs text-neutral-400">
        <div className="h-px flex-1 bg-neutral-200" />
        or
        <div className="h-px flex-1 bg-neutral-200" />
      </div>

      {/* Plain navigation, not a Server Action — this has to be a real
          browser redirect to Google, not a fetch/form-post. */}
      <a
        href="/api/auth/google"
        className="flex w-full items-center justify-center rounded-md border border-neutral-300 px-4 py-2 text-sm font-medium hover:bg-neutral-50"
      >
        Sign in with Google
      </a>
    </form>
  );
}
