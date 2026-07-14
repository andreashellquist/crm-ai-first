import Link from "next/link";
import { requireWorkspace } from "@/lib/workspace";
import { signOut } from "@/lib/auth";
import { Button } from "@/components/ui/button";

export default async function AppLayout({ children }: { children: React.ReactNode }) {
  const { workspace } = await requireWorkspace();

  return (
    <div className="flex flex-1 flex-col">
      <header className="flex items-center justify-between border-b border-neutral-200 px-6 py-3">
        <div className="flex items-center gap-6">
          <span className="font-semibold">{workspace.name}</span>
          <nav className="flex gap-4 text-sm text-neutral-600">
            <Link href="/contacts" className="hover:text-neutral-950">
              Contacts
            </Link>
            <Link href="/pipeline" className="hover:text-neutral-950">
              Pipeline
            </Link>
          </nav>
        </div>
        <form
          action={async () => {
            "use server";
            await signOut({ redirectTo: "/login" });
          }}
        >
          <Button variant="ghost" size="sm" type="submit">
            Sign out
          </Button>
        </form>
      </header>
      <main className="flex-1 p-6">{children}</main>
    </div>
  );
}
