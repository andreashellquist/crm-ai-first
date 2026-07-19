"use client";

import { useTransition } from "react";
import { removeMemberAction, updateMemberRoleAction } from "./actions";
import { Button } from "@/components/ui/button";
import type { components } from "@/lib/api/schema";

type Member = components["schemas"]["MemberDto"];
type Role = components["schemas"]["RoleDto"];

export function MembersSection({ members, roles }: { members: Member[]; roles: Role[] }) {
  const [, startTransition] = useTransition();

  return (
    <ul className="space-y-2">
      {members.map((member) => (
        <li key={member.id} className="flex items-center justify-between rounded-md border border-neutral-200 p-3 text-sm">
          <div>
            <div className="font-medium">
              {member.name ?? member.email} {!member.isActive ? <span className="text-xs text-red-600">(inactive)</span> : null}
            </div>
            <div className="text-xs text-neutral-500">{member.email}</div>
          </div>
          <div className="flex items-center gap-2">
            <select
              defaultValue={member.role}
              className="rounded border border-neutral-300 bg-white px-2 py-1 text-xs"
              onChange={(e) => startTransition(() => updateMemberRoleAction(member.id, e.target.value))}
              aria-label={`Role for ${member.name ?? member.email}`}
            >
              {roles.map((role) => (
                <option key={role.name} value={role.name}>
                  {role.name}
                </option>
              ))}
            </select>
            <Button type="button" variant="ghost" size="sm" onClick={() => startTransition(() => removeMemberAction(member.id))}>
              Remove
            </Button>
          </div>
        </li>
      ))}
    </ul>
  );
}
