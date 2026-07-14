"use client";

import { useActionState, useEffect, useRef } from "react";
import { createContactAction, type CreateContactState } from "./actions";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";

const initialState: CreateContactState = {};

export function NewContactForm() {
  const [state, formAction, pending] = useActionState(createContactAction, initialState);
  const formRef = useRef<HTMLFormElement>(null);

  useEffect(() => {
    if (!pending && !state.error) formRef.current?.reset();
  }, [pending, state]);

  return (
    <form ref={formRef} action={formAction} className="flex flex-wrap items-end gap-3">
      <div className="space-y-1">
        <label htmlFor="firstName" className="text-xs font-medium text-neutral-600">
          First name
        </label>
        <Input id="firstName" name="firstName" required className="w-40" />
      </div>
      <div className="space-y-1">
        <label htmlFor="lastName" className="text-xs font-medium text-neutral-600">
          Last name
        </label>
        <Input id="lastName" name="lastName" className="w-40" />
      </div>
      <div className="space-y-1">
        <label htmlFor="email" className="text-xs font-medium text-neutral-600">
          Email
        </label>
        <Input id="email" name="email" type="email" className="w-56" />
      </div>
      <div className="space-y-1">
        <label htmlFor="companyName" className="text-xs font-medium text-neutral-600">
          Company
        </label>
        <Input id="companyName" name="companyName" className="w-48" />
      </div>
      <Button type="submit" disabled={pending}>
        {pending ? "Adding…" : "Add contact"}
      </Button>
      {state.error ? <p className="w-full text-sm text-red-600">{state.error}</p> : null}
    </form>
  );
}
