"use client";

import { useActionState } from "react";
import { loginAction } from "./actions";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";

export default function LoginPage() {
  const [error, formAction, pending] = useActionState(loginAction, undefined);

  return (
    <main className="flex flex-1 items-center justify-center p-6">
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

        <Button type="submit" className="w-full" disabled={pending}>
          {pending ? "Signing in…" : "Sign in"}
        </Button>
      </form>
    </main>
  );
}
