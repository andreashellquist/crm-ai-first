import { requireWorkspace } from "@/lib/workspace";
import { SettingsForm } from "./settings-form";

const KNOWN_MODULES = [
  { id: "listings", name: "Listings", description: "Real-estate deal extension — MLS listing agent, URL, open house, commission." },
];

export default async function SettingsPage() {
  const { api } = await requireWorkspace();
  const { data: settings } = await api.GET("/api/workspace/settings");

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
        <h2 className="text-sm font-medium">Terminology</h2>
        {overrides.length === 0 ? (
          <p className="text-sm text-neutral-500">No overrides — this workspace uses the default CRM vocabulary.</p>
        ) : (
          <ul className="space-y-1 text-sm text-neutral-700">
            {overrides.map(([key, value]) => {
              const term = value as { singular?: string; plural?: string; label?: string };
              return (
                <li key={key}>
                  <span className="font-mono text-xs text-neutral-400">{key}</span>{" "}
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
    </div>
  );
}
