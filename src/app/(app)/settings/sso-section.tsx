"use client";

import { useActionState, useTransition } from "react";
import { updateSsoAction, deleteSsoAction } from "./actions";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import type { components } from "@/lib/api/schema";

type SsoConnection = components["schemas"]["SsoConnectionDto"];

export function SsoSection({ connection }: { connection: SsoConnection | null }) {
  const [state, formAction, pending] = useActionState(updateSsoAction, {});
  const [deleting, startDeleteTransition] = useTransition();

  return (
    <div className="space-y-4">
      {connection ? (
        <div className="flex items-center justify-between rounded-md border border-neutral-200 p-3 text-sm">
          <div>
            <span className="font-medium">{connection.emailDomain}</span>{" "}
            <span className="text-neutral-500">→ {connection.issuer}</span>
            <span className="ml-2 text-xs text-neutral-500">
              {connection.enforced ? "Required for this domain" : "Optional — password login still works"}
            </span>
          </div>
          <Button
            type="button"
            variant="outline"
            size="sm"
            disabled={deleting}
            onClick={() => startDeleteTransition(() => deleteSsoAction())}
          >
            {deleting ? "Removing…" : "Remove"}
          </Button>
        </div>
      ) : (
        <p className="text-sm text-neutral-500">No SSO connection configured.</p>
      )}

      <form action={formAction} className="space-y-3 rounded-md border border-neutral-200 p-3">
        <p className="text-xs text-neutral-500">
          {connection ? "Replace the connection" : "Connect an OIDC identity provider"} — the client secret must be
          re-entered every time, it&apos;s never shown back after saving.
        </p>
        <div className="grid grid-cols-2 gap-3">
          <label className="text-sm">
            <span className="block text-neutral-500">Issuer (https://)</span>
            <Input name="issuer" type="url" required defaultValue={connection?.issuer} placeholder="https://acme.okta.com" />
          </label>
          <label className="text-sm">
            <span className="block text-neutral-500">Email domain</span>
            <Input name="emailDomain" required defaultValue={connection?.emailDomain} placeholder="acme.com" />
          </label>
          <label className="text-sm">
            <span className="block text-neutral-500">Client ID</span>
            <Input name="clientId" required defaultValue={connection?.clientId} />
          </label>
          <label className="text-sm">
            <span className="block text-neutral-500">Client secret</span>
            <Input name="clientSecret" type="password" required placeholder={connection ? "••••••••" : ""} />
          </label>
        </div>
        <label className="flex items-center gap-2 text-sm">
          <input type="checkbox" name="enforced" defaultChecked={connection?.enforced} />
          Require SSO for this domain (disables password login for matching emails)
        </label>

        {state.error ? <p className="text-sm text-red-600">{state.error}</p> : null}

        <Button type="submit" disabled={pending}>
          {pending ? "Saving…" : "Save SSO connection"}
        </Button>
      </form>
    </div>
  );
}
