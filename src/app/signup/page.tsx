import { createApiClient } from "@/lib/api/client";
import { SignupForm } from "./signup-form";

export default async function SignupPage() {
  // Public endpoint (no session yet) — see AuthController.Templates. Static
  // in-repo product content, safe to fetch on every page load.
  const client = createApiClient();
  const { data: templates } = await client.GET("/api/auth/templates");

  return (
    <main className="flex flex-1 items-center justify-center p-6">
      <SignupForm templates={templates ?? []} />
    </main>
  );
}
