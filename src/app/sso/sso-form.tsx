"use client";

import { useSearchParams } from "next/navigation";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";

const ERROR_MESSAGES: Record<string, string> = {
  missing_email: "Enter your email address.",
  not_configured: "No single sign-on connection is set up for that email's domain.",
};

export function SsoForm() {
  const searchParams = useSearchParams();
  const error = searchParams.get("error");

  return (
    // Plain form POST (works without JS) to a Route Handler, not a Server
    // Action — the handler needs to end in a redirect straight to a
    // third-party IdP URL, which a Server Action can't do as a real browser
    // navigation the way a Route Handler's NextResponse.redirect can.
    <form action="/api/auth/sso/start" method="post" className="space-y-4">
      <div className="space-y-1">
        <label htmlFor="email" className="text-sm font-medium">
          Work email
        </label>
        <Input id="email" name="email" type="email" required autoComplete="email" placeholder="you@company.com" />
      </div>

      {error ? <p className="text-sm text-red-600">{ERROR_MESSAGES[error] ?? "Sign-in failed — try again."}</p> : null}

      <Button type="submit" className="w-full">
        Continue
      </Button>
    </form>
  );
}
