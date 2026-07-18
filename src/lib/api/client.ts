import createClient from "openapi-fetch";
import type { paths } from "./schema";

const API_BASE_URL = process.env.API_BASE_URL ?? "http://localhost:5194";

// Next.js is a thin frontend/BFF here — this client is used server-side only
// (Server Components / Server Actions calling the .NET API), never shipped
// to the browser. The JWT it carries never reaches client JS.
export function createApiClient(token?: string) {
  return createClient<paths>({
    baseUrl: API_BASE_URL,
    headers: token ? { Authorization: `Bearer ${token}` } : undefined,
  });
}
