"use client";

import { useTransition } from "react";
import { assignDealAction } from "./actions";

export function AssignDealSelect({
  dealId,
  assignedToUserId,
  members,
}: {
  dealId: string;
  assignedToUserId: string | null;
  members: { userId: string; name: string | null; email: string }[];
}) {
  const [pending, startTransition] = useTransition();

  return (
    <label className="flex items-center gap-2 text-sm">
      <span className="text-neutral-500">Assigned to</span>
      <select
        defaultValue={assignedToUserId ?? ""}
        disabled={pending}
        onChange={(e) => startTransition(() => assignDealAction(dealId, e.target.value || null))}
        className="rounded border border-neutral-300 bg-white px-2 py-1 text-sm"
      >
        <option value="">Unassigned</option>
        {members.map((member) => (
          <option key={member.userId} value={member.userId}>
            {member.name ?? member.email}
          </option>
        ))}
      </select>
    </label>
  );
}
