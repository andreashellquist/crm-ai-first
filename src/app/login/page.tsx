import { Suspense } from "react";
import { LoginForm } from "./login-form";

export default function LoginPage() {
  return (
    <main className="flex flex-1 items-center justify-center p-6">
      {/* useSearchParams() (reading ?error=... from the OAuth callback
          redirect) requires a Suspense boundary for static prerendering. */}
      <Suspense>
        <LoginForm />
      </Suspense>
    </main>
  );
}
