"use client";

import { useActionState, useTransition } from "react";
import { createApiKeyAction, revokeApiKeyAction, type CreateApiKeyState } from "./actions";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import type { components } from "@/lib/api/schema";

type ApiKey = components["schemas"]["ApiKeyDto"];

const SCOPES = ["contacts:read", "companies:read", "deals:read", "field-definitions:read"];

const initialState: CreateApiKeyState = {};

export function ApiKeysSection({ apiKeys }: { apiKeys: ApiKey[] }) {
  const [state, formAction, pending] = useActionState(createApiKeyAction, initialState);
  const [, startTransition] = useTransition();

  return (
    <div className="space-y-4">
      <p className="text-sm text-neutral-500">
        API keys authenticate the public v1 API (<code className="font-mono text-xs">X-Api-Key</code> header) for a
        customer&apos;s own integrations — see <code className="font-mono text-xs">GET /api/v1/contacts</code> and
        friends.
      </p>

      {apiKeys.length === 0 ? (
        <p className="text-sm text-neutral-500">No API keys yet.</p>
      ) : (
        <ul className="space-y-2">
          {apiKeys.map((key) => (
            <li key={key.id} className="flex items-center justify-between rounded-md border border-neutral-200 p-3 text-sm">
              <div>
                <div className="font-medium">
                  {key.name} {key.revokedAt ? <span className="text-xs text-red-600">(revoked)</span> : null}
                </div>
                <div className="text-xs text-neutral-500">
                  {key.scopes.join(", ")} · {key.lastUsedAt ? `last used ${new Date(key.lastUsedAt).toLocaleString()}` : "never used"}
                </div>
              </div>
              {!key.revokedAt ? (
                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  onClick={() => startTransition(() => revokeApiKeyAction(key.id))}
                >
                  Revoke
                </Button>
              ) : null}
            </li>
          ))}
        </ul>
      )}

      {state.rawKey ? (
        <div className="rounded-md border border-amber-300 bg-amber-50 p-3 text-sm">
          <p className="font-medium text-amber-900">
            &quot;{state.keyName}&quot; created — copy this key now, it won&apos;t be shown again:
          </p>
          <code className="mt-1 block break-all rounded bg-white px-2 py-1 font-mono text-xs">{state.rawKey}</code>
        </div>
      ) : null}

      <form action={formAction} className="space-y-2 rounded-md border border-neutral-200 p-3">
        <div className="space-y-1">
          <label htmlFor="apiKeyName" className="text-xs font-medium text-neutral-600">
            Key name
          </label>
          <Input id="apiKeyName" name="name" placeholder="e.g. Zapier integration" required />
        </div>
        <div className="space-y-1">
          <span className="text-xs font-medium text-neutral-600">Scopes</span>
          <div className="flex flex-wrap gap-3">
            {SCOPES.map((scope) => (
              <label key={scope} className="flex items-center gap-1 text-xs">
                <input type="checkbox" name="scopes" value={scope} />
                {scope}
              </label>
            ))}
          </div>
        </div>
        {state.error ? <p className="text-sm text-red-600">{state.error}</p> : null}
        <Button type="submit" size="sm" disabled={pending}>
          {pending ? "Creating…" : "Create key"}
        </Button>
      </form>
    </div>
  );
}
