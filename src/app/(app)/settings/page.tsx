import { requireWorkspace } from "@/lib/workspace";
import { SettingsForm } from "./settings-form";
import { CurrencyForm } from "./currency-form";
import { MyProfileForm } from "./my-profile-form";
import { ApiKeysSection } from "./api-keys-section";
import { WebhooksSection } from "./webhooks-section";
import { RolesSection } from "./roles-section";
import { MembersSection } from "./members-section";

const KNOWN_MODULES = [
  { id: "listings", name: "Listings", description: "Real-estate deal extension — MLS listing agent, URL, open house, commission." },
];

export default async function SettingsPage() {
  const { api } = await requireWorkspace();
  const { data: settings } = await api.GET("/api/workspace/settings");
  const { data: apiKeys } = await api.GET("/api/api-keys");
  const { data: webhookSubscriptions } = await api.GET("/api/webhook-subscriptions");
  const { data: roles } = await api.GET("/api/roles");
  const { data: members } = await api.GET("/api/members");
  const { data: me } = await api.GET("/api/me");

  const terminology = settings?.terminology ?? {};
  const overrides = Object.entries(terminology).filter(([, v]) => v && typeof v === "object");

  return (
    <div className="mx-auto max-w-2xl space-y-8">
      <div>
        <h1 className="text-xl font-semibold">Workspace settings</h1>
        <p className="text-sm text-neutral-500">
          Terminology and modules are set once at signup from your starter template — this page lets an owner or
          admin turn optional modules on or off afterward.
        </p>
      </div>

      <section className="space-y-2">
        <h2 className="text-sm font-medium">My profile</h2>
        <p className="text-sm text-neutral-500">
          Your timezone is used to display times like a report&apos;s last-refreshed timestamp in your own local time.
        </p>
        <MyProfileForm name={me?.name ?? null} timezone={me?.timezone ?? "UTC"} />
      </section>

      <section className="space-y-2">
        <h2 className="text-sm font-medium">Default currency</h2>
        <p className="text-sm text-neutral-500">
          Used to format deal amounts that don&apos;t specify their own currency, and to label pipeline/report totals.
        </p>
        <CurrencyForm defaultCurrency={settings?.defaultCurrency ?? "USD"} />
      </section>

      <section className="space-y-2">
        <h2 className="text-sm font-medium">Terminology</h2>
        {overrides.length === 0 ? (
          <p className="text-sm text-neutral-500">No overrides — this workspace uses the default CRM vocabulary.</p>
        ) : (
          <ul className="space-y-1 text-sm text-neutral-700">
            {overrides.map(([key, value]) => {
              const term = value as { singular?: string; plural?: string; label?: string };
              return (
                <li key={key}>
                  <span className="font-mono text-xs text-neutral-500">{key}</span>{" "}
                  {term.singular ?? term.label}
                  {term.plural ? ` / ${term.plural}` : ""}
                </li>
              );
            })}
          </ul>
        )}
      </section>

      <section className="space-y-2">
        <h2 className="text-sm font-medium">Optional modules</h2>
        <p className="text-sm text-neutral-500">
          Core flows (contacts, deals, pipeline, activities, tasks, AI features) work the same whether or not any
          module is enabled.
        </p>
        <SettingsForm modules={KNOWN_MODULES} enabledModules={settings?.enabledModules ?? []} />
      </section>

      <section className="space-y-2">
        <h2 className="text-sm font-medium">Members</h2>
        <MembersSection members={members ?? []} roles={roles ?? []} />
      </section>

      <section className="space-y-2">
        <h2 className="text-sm font-medium">Roles</h2>
        <RolesSection roles={roles ?? []} />
      </section>

      <section className="space-y-2">
        <h2 className="text-sm font-medium">API keys</h2>
        <ApiKeysSection apiKeys={apiKeys ?? []} />
      </section>

      <section className="space-y-2">
        <h2 className="text-sm font-medium">Webhooks</h2>
        <WebhooksSection subscriptions={webhookSubscriptions ?? []} />
      </section>
    </div>
  );
}
