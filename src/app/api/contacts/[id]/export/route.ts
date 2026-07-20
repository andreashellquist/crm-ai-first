import { NextResponse } from "next/server";
import { getSession } from "@/lib/session";

const API_BASE_URL = process.env.API_BASE_URL ?? "http://localhost:5194";

// Same server-to-server proxy pattern as /api/reports/export — the browser
// never talks to the .NET API directly. Fulfills a data-subject access
// request as a downloadable JSON file (see ContactsController.Export /
// auth-security-expert's "Data-subject requests" section).
export async function GET(request: Request, { params }: { params: Promise<{ id: string }> }) {
  const session = await getSession();
  if (!session) return NextResponse.redirect(new URL("/login", request.url));

  const { id } = await params;
  const upstream = await fetch(`${API_BASE_URL}/api/contacts/${encodeURIComponent(id)}/export`, {
    headers: { Authorization: `Bearer ${session.token}` },
  });
  if (!upstream.ok) {
    return NextResponse.json({ error: "Could not export this contact's data" }, { status: upstream.status });
  }

  return new NextResponse(upstream.body, {
    headers: {
      "Content-Type": "application/json",
      "Content-Disposition": `attachment; filename="contact-${id}-export.json"`,
    },
  });
}
