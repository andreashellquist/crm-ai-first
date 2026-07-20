import { Suspense } from "react";
import Link from "next/link";
import { SsoForm } from "./sso-form";

export default function SsoPage() {
  return (
    <main className="flex flex-1 items-center justify-center p-6">
      <div className="w-full max-w-sm space-y-4 rounded-lg border border-neutral-200 p-6">
        <div className="space-y-1">
          <h1 className="text-lg font-semibold">Sign in with SSO</h1>
          <p className="text-sm text-neutral-500">
            Enter your work email — we&apos;ll redirect you to your company&apos;s sign-in page if single sign-on is
            set up for your workspace.
          </p>
        </div>
        <Suspense>
          <SsoForm />
        </Suspense>
        <p className="text-center text-xs text-neutral-500">
          <Link href="/login" className="underline">
            Back to password sign-in
          </Link>
        </p>
      </div>
    </main>
  );
}
