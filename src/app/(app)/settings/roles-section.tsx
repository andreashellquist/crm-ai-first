"use client";

import { useActionState, useTransition } from "react";
import { createRoleAction, deleteRoleAction, updateRolePermissionsAction, type CreateRoleState } from "./actions";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import type { components } from "@/lib/api/schema";

type Role = components["schemas"]["RoleDto"];

const PERMISSIONS = ["settings:manage", "fields:manage", "api_keys:manage", "webhooks:manage", "members:manage", "roles:manage"];

const initialState: CreateRoleState = {};

function CustomRoleRow({ role }: { role: Role }) {
  const [, startTransition] = useTransition();

  function togglePermission(permission: string, checked: boolean) {
    const next = checked ? [...role.permissions, permission] : role.permissions.filter((p) => p !== permission);
    startTransition(() => updateRolePermissionsAction(role.id!, next));
  }

  return (
    <li className="space-y-2 rounded-md border border-neutral-200 p-3 text-sm">
      <div className="flex items-center justify-between">
        <span className="font-medium">{role.name}</span>
        <Button type="button" variant="ghost" size="sm" onClick={() => startTransition(() => deleteRoleAction(role.id!))}>
          Delete
        </Button>
      </div>
      <div className="flex flex-wrap gap-3">
        {PERMISSIONS.map((permission) => (
          <label key={permission} className="flex items-center gap-1 text-xs">
            <input
              type="checkbox"
              defaultChecked={role.permissions.includes(permission)}
              onChange={(e) => togglePermission(permission, e.target.checked)}
            />
            {permission}
          </label>
        ))}
      </div>
    </li>
  );
}

export function RolesSection({ roles }: { roles: Role[] }) {
  const [state, formAction, pending] = useActionState(createRoleAction, initialState);
  const systemRoles = roles.filter((r) => r.isSystem);
  const customRoles = roles.filter((r) => !r.isSystem);

  return (
    <div className="space-y-4">
      <p className="text-sm text-neutral-500">
        The <code className="font-mono text-xs">owner</code>/<code className="font-mono text-xs">admin</code>/
        <code className="font-mono text-xs">member</code> system roles are fixed. Custom roles grant an explicit
        subset of permissions — only an owner can create or edit roles.
      </p>

      <ul className="space-y-1 text-sm text-neutral-600">
        {systemRoles.map((role) => (
          <li key={role.name}>
            <span className="font-medium">{role.name}</span> — {role.permissions.length === 0 ? "no admin permissions" : role.permissions.join(", ")}
          </li>
        ))}
      </ul>

      {customRoles.length > 0 ? (
        <ul className="space-y-2">
          {customRoles.map((role) => (
            <CustomRoleRow key={role.id} role={role} />
          ))}
        </ul>
      ) : null}

      <form action={formAction} className="space-y-2 rounded-md border border-neutral-200 p-3">
        <div className="space-y-1">
          <label htmlFor="roleName" className="text-xs font-medium text-neutral-600">
            New role name
          </label>
          <Input id="roleName" name="name" placeholder="e.g. Field Editor" required />
        </div>
        <div className="space-y-1">
          <span className="text-xs font-medium text-neutral-600">Permissions</span>
          <div className="flex flex-wrap gap-3">
            {PERMISSIONS.map((permission) => (
              <label key={permission} className="flex items-center gap-1 text-xs">
                <input type="checkbox" name="permissions" value={permission} />
                {permission}
              </label>
            ))}
          </div>
        </div>
        {state.error ? <p className="text-sm text-red-600">{state.error}</p> : null}
        <Button type="submit" size="sm" disabled={pending}>
          {pending ? "Creating…" : "Create role"}
        </Button>
      </form>
    </div>
  );
}
