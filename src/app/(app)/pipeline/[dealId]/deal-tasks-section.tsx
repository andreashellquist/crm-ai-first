"use client";

import { useActionState, useEffect, useRef, useTransition } from "react";
import { createTaskAction, deleteTaskAction, toggleTaskCompleteAction, type CreateTaskState } from "./actions";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";

type Task = {
  id: string;
  title: string;
  dueAt: string | null;
  completedAt: string | null;
  assignedToUserId: string | null;
  assignedToUserName: string | null;
};

const initialState: CreateTaskState = {};

// TasksController (List/Create/Update/Delete) has existed since Phase 0 —
// this is the first frontend anywhere in the app to actually call it. Kept
// deal-scoped rather than a standalone /tasks page: proportional to what
// unblocking the task_overdue notification trigger actually needs.
export function DealTasksSection({
  dealId,
  tasks,
  members,
}: {
  dealId: string;
  tasks: Task[];
  members: { userId: string; name: string | null; email: string }[];
}) {
  const [state, formAction, pending] = useActionState(createTaskAction, initialState);
  const formRef = useRef<HTMLFormElement>(null);
  const [, startTransition] = useTransition();

  useEffect(() => {
    if (!pending && !state.error) formRef.current?.reset();
  }, [pending, state]);

  return (
    <div className="space-y-3">
      <h2 className="text-sm font-semibold text-neutral-700">Tasks</h2>

      {tasks.length === 0 ? (
        <p className="text-sm text-neutral-500">No open tasks.</p>
      ) : (
        <ul className="space-y-2">
          {tasks.map((task) => (
            <li key={task.id} className="flex items-center justify-between rounded border border-neutral-200 p-2 text-sm">
              <label className="flex items-center gap-2">
                <input
                  type="checkbox"
                  checked={!!task.completedAt}
                  onChange={(e) =>
                    startTransition(() =>
                      toggleTaskCompleteAction(dealId, task.id, task.title, task.dueAt, task.assignedToUserId, e.target.checked),
                    )
                  }
                />
                <span>
                  {task.title}
                  {task.dueAt ? <span className="ml-2 text-xs text-neutral-500">due {new Date(task.dueAt).toLocaleDateString()}</span> : null}
                  {task.assignedToUserName ? <span className="ml-2 text-xs text-neutral-500">· {task.assignedToUserName}</span> : null}
                </span>
              </label>
              <Button type="button" variant="ghost" size="sm" onClick={() => startTransition(() => deleteTaskAction(dealId, task.id))}>
                Delete
              </Button>
            </li>
          ))}
        </ul>
      )}

      <form ref={formRef} action={formAction} className="flex flex-wrap items-end gap-2">
        <input type="hidden" name="dealId" value={dealId} />
        <div className="space-y-1">
          <label htmlFor="taskTitle" className="text-xs font-medium text-neutral-600">
            New task
          </label>
          <Input id="taskTitle" name="title" required className="w-48" />
        </div>
        <div className="space-y-1">
          <label htmlFor="taskDueAt" className="text-xs font-medium text-neutral-600">
            Due
          </label>
          <Input id="taskDueAt" name="dueAt" type="date" className="w-40" />
        </div>
        <div className="space-y-1">
          <label htmlFor="taskAssignee" className="text-xs font-medium text-neutral-600">
            Assign to
          </label>
          <select id="taskAssignee" name="assignedToUserId" className="h-9 rounded-md border border-neutral-300 bg-white px-2 text-sm" defaultValue="">
            <option value="">Unassigned</option>
            {members.map((member) => (
              <option key={member.userId} value={member.userId}>
                {member.name ?? member.email}
              </option>
            ))}
          </select>
        </div>
        <Button type="submit" disabled={pending}>
          {pending ? "Adding…" : "Add task"}
        </Button>
      </form>
      {state.error ? <p className="text-sm text-red-600">{state.error}</p> : null}
    </div>
  );
}
