"use client";

import { useActionState, useState, useTransition } from "react";
import {
  createWebhookAction,
  deleteWebhookAction,
  fetchWebhookDeliveriesAction,
  regenerateWebhookSecretAction,
  toggleWebhookAction,
  type CreateWebhookState,
  type WebhookDelivery,
} from "./actions";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import type { components } from "@/lib/api/schema";

type WebhookSubscription = components["schemas"]["WebhookSubscriptionDto"];

const EVENT_TYPES = ["deal.won", "deal.lost", "deal.stage_changed", "contact.created"];

const initialState: CreateWebhookState = {};

function DeliveryHistory({ subscriptionId }: { subscriptionId: string }) {
  const [deliveries, setDeliveries] = useState<WebhookDelivery[] | null>(null);
  const [, startTransition] = useTransition();

  if (deliveries === null) {
    return (
      <button
        type="button"
        className="text-xs text-neutral-500 underline"
        onClick={() => startTransition(async () => setDeliveries(await fetchWebhookDeliveriesAction(subscriptionId)))}
      >
        View recent deliveries
      </button>
    );
  }

  if (deliveries.length === 0) return <p className="text-xs text-neutral-500">No deliveries yet.</p>;

  return (
    <ul className="space-y-1 text-xs text-neutral-600">
      {deliveries.map((d) => (
        <li key={d.id}>
          {d.eventType} — {d.status} ({d.attempts} attempt{d.attempts === 1 ? "" : "s"})
          {d.lastAttemptAt ? ` · ${new Date(d.lastAttemptAt).toLocaleString()}` : ""}
        </li>
      ))}
    </ul>
  );
}

export function WebhooksSection({ subscriptions }: { subscriptions: WebhookSubscription[] }) {
  const [state, formAction, pending] = useActionState(createWebhookAction, initialState);
  const [, startTransition] = useTransition();
  const [regeneratedSecret, setRegeneratedSecret] = useState<string | null>(null);

  return (
    <div className="space-y-4">
      <p className="text-sm text-neutral-500">
        Outbound webhooks POST a signed JSON payload (
        <code className="font-mono text-xs">X-Crm-Signature: sha256=…</code>) to your URL when one of these business
        events happens.
      </p>

      {subscriptions.length === 0 ? (
        <p className="text-sm text-neutral-500">No webhook subscriptions yet.</p>
      ) : (
        <ul className="space-y-2">
          {subscriptions.map((sub) => (
            <li key={sub.id} className="space-y-2 rounded-md border border-neutral-200 p-3 text-sm">
              <div className="flex items-center justify-between">
                <div>
                  <div className="font-medium break-all">
                    {sub.url} {!sub.isActive ? <span className="text-xs text-neutral-500">(inactive)</span> : null}
                  </div>
                  <div className="text-xs text-neutral-500">{sub.eventTypes.join(", ")}</div>
                </div>
                <div className="flex shrink-0 gap-1">
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    onClick={() => startTransition(() => toggleWebhookAction(sub.id, sub.eventTypes, !sub.isActive))}
                  >
                    {sub.isActive ? "Deactivate" : "Activate"}
                  </Button>
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    onClick={() =>
                      startTransition(async () => setRegeneratedSecret(await regenerateWebhookSecretAction(sub.id)))
                    }
                  >
                    Regenerate secret
                  </Button>
                  <Button type="button" variant="ghost" size="sm" onClick={() => startTransition(() => deleteWebhookAction(sub.id))}>
                    Delete
                  </Button>
                </div>
              </div>
              <DeliveryHistory subscriptionId={sub.id} />
            </li>
          ))}
        </ul>
      )}

      {regeneratedSecret ? (
        <div className="rounded-md border border-amber-300 bg-amber-50 p-3 text-sm">
          <p className="font-medium text-amber-900">New secret — copy it now, it won&apos;t be shown again:</p>
          <code className="mt-1 block break-all rounded bg-white px-2 py-1 font-mono text-xs">{regeneratedSecret}</code>
        </div>
      ) : null}

      {state.secret ? (
        <div className="rounded-md border border-amber-300 bg-amber-50 p-3 text-sm">
          <p className="font-medium text-amber-900">
            Webhook for {state.url} created — copy this secret now, it won&apos;t be shown again:
          </p>
          <code className="mt-1 block break-all rounded bg-white px-2 py-1 font-mono text-xs">{state.secret}</code>
        </div>
      ) : null}

      <form action={formAction} className="space-y-2 rounded-md border border-neutral-200 p-3">
        <div className="space-y-1">
          <label htmlFor="webhookUrl" className="text-xs font-medium text-neutral-600">
            Endpoint URL
          </label>
          <Input id="webhookUrl" name="url" type="url" placeholder="https://example.com/hooks/crm" required />
        </div>
        <div className="space-y-1">
          <span className="text-xs font-medium text-neutral-600">Events</span>
          <div className="flex flex-wrap gap-3">
            {EVENT_TYPES.map((eventType) => (
              <label key={eventType} className="flex items-center gap-1 text-xs">
                <input type="checkbox" name="eventTypes" value={eventType} />
                {eventType}
              </label>
            ))}
          </div>
        </div>
        {state.error ? <p className="text-sm text-red-600">{state.error}</p> : null}
        <Button type="submit" size="sm" disabled={pending}>
          {pending ? "Creating…" : "Add webhook"}
        </Button>
      </form>
    </div>
  );
}
