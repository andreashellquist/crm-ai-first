"use client";

import { useActionState, useState } from "react";
import Link from "next/link";
import { registerAction } from "./actions";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import type { components } from "@/lib/api/schema";

type VerticalTemplate = components["schemas"]["VerticalTemplateSummaryDto"];

export function SignupForm({ templates }: { templates: VerticalTemplate[] }) {
  const [error, formAction, pending] = useActionState(registerAction, undefined);
  const defaultTemplateId = templates.find((t) => t.id === "saas-sales")?.id ?? templates[0]?.id;
  const [selectedId, setSelectedId] = useState<string | undefined>(defaultTemplateId);

  return (
    <form action={formAction} className="w-full max-w-lg space-y-6 rounded-lg border border-neutral-200 p-6">
      <div className="space-y-1">
        <h1 className="text-lg font-semibold">Create your workspace</h1>
        <p className="text-sm text-neutral-500">
          Already have an account?{" "}
          <Link href="/login" className="underline">
            Sign in
          </Link>
        </p>
      </div>

      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <div className="space-y-1">
          <label htmlFor="name" className="text-sm font-medium">
            Your name
          </label>
          <Input id="name" name="name" required autoComplete="name" />
        </div>
        <div className="space-y-1">
          <label htmlFor="workspaceName" className="text-sm font-medium">
            Workspace name
          </label>
          <Input id="workspaceName" name="workspaceName" required autoComplete="organization" />
        </div>
        <div className="space-y-1">
          <label htmlFor="email" className="text-sm font-medium">
            Email
          </label>
          <Input id="email" name="email" type="email" required autoComplete="email" />
        </div>
        <div className="space-y-1">
          <label htmlFor="password" className="text-sm font-medium">
            Password
          </label>
          <Input id="password" name="password" type="password" required minLength={8} autoComplete="new-password" />
        </div>
      </div>

      <div className="space-y-2">
        <span className="text-sm font-medium">Starter template</span>
        <p className="text-xs text-neutral-500">
          Picks your pipeline stages and vocabulary — everything here stays editable afterward.
        </p>
        <input type="hidden" name="templateId" value={selectedId ?? ""} />
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
          {templates.map((template) => {
            const selected = template.id === selectedId;
            return (
              <button
                key={template.id}
                type="button"
                onClick={() => setSelectedId(template.id)}
                aria-pressed={selected}
                className={`rounded-md border p-3 text-left text-sm transition-colors ${
                  selected ? "border-neutral-900 bg-neutral-50" : "border-neutral-200 hover:border-neutral-400"
                }`}
              >
                <div className="font-medium">{template.name}</div>
                <div className="mt-1 text-xs text-neutral-500">{template.description}</div>
                <div className="mt-2 text-xs text-neutral-400">
                  {template.dealTerm} pipeline: {template.stages.join(" → ")}
                </div>
              </button>
            );
          })}
        </div>
      </div>

      {error ? <p className="text-sm text-red-600">{error}</p> : null}

      <Button type="submit" className="w-full" disabled={pending || !selectedId}>
        {pending ? "Creating workspace…" : "Create workspace"}
      </Button>
    </form>
  );
}
