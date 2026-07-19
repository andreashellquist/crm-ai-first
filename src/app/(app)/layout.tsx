import Link from "next/link";
import { redirect } from "next/navigation";
import { requireWorkspace } from "@/lib/workspace";
import { clearSession } from "@/lib/session";
import { Button } from "@/components/ui/button";
import { NotificationBell } from "./notification-bell";

export default async function AppLayout({ children }: { children: React.ReactNode }) {
  const { workspaceName } = await requireWorkspace();

  return (
    <div className="flex flex-1 flex-col">
      <header className="flex items-center justify-between border-b border-neutral-200 px-6 py-3">
        <div className="flex items-center gap-6">
          <span className="font-semibold">{workspaceName}</span>
          <nav className="flex gap-4 text-sm text-neutral-600">
            <Link href="/contacts" className="hover:text-neutral-950">
              Contacts
            </Link>
            <Link href="/pipeline" className="hover:text-neutral-950">
              Pipeline
            </Link>
          </nav>
        </div>
        <div className="flex items-center gap-3">
          <NotificationBell />
          <form
            action={async () => {
              "use server";
              await clearSession();
              redirect("/login");
            }}
          >
            <Button variant="ghost" size="sm" type="submit">
              Sign out
            </Button>
          </form>
        </div>
      </header>
      <main className="flex-1 p-6">{children}</main>
    </div>
  );
}
