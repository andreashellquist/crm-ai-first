import { NextResponse } from "next/server";
import { getSession } from "@/lib/session";

const API_BASE_URL = process.env.API_BASE_URL ?? "http://localhost:5194";
const VALID_TYPES = ["pipeline", "forecast", "activity"];

// A plain <a href> can't call the .NET API directly — the browser never
// talks to it, per this app's architecture (no CORS surface, the session
// JWT never reaches client JS; see CLAUDE.md's "Chosen stack"). This route
// forwards the request server-to-server with the session token, same as
// every other backend call, and streams the CSV response back through.
export async function GET(request: Request) {
  const session = await getSession();
  if (!session) return NextResponse.redirect(new URL("/login", request.url));

  const type = new URL(request.url).searchParams.get("type") ?? "";
  if (!VALID_TYPES.includes(type)) {
    return NextResponse.json({ error: "Invalid export type" }, { status: 400 });
  }

  const upstream = await fetch(`${API_BASE_URL}/api/reports/export?type=${encodeURIComponent(type)}`, {
    headers: { Authorization: `Bearer ${session.token}` },
  });
  if (!upstream.ok) {
    return NextResponse.json({ error: "Could not export report" }, { status: upstream.status });
  }

  return new NextResponse(upstream.body, {
    headers: {
      "Content-Type": "text/csv",
      "Content-Disposition": upstream.headers.get("Content-Disposition") ?? `attachment; filename="${type}-report.csv"`,
    },
  });
}
