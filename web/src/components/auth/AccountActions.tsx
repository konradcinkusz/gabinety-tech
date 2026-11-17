"use client";
import { useState, type FormEvent } from "react";
import { Alert, Button, Card, Field, TextInput } from "@/components/ui";
import { at } from "./strings";

export type AccountActionHandlers = {
  accept: () => Promise<void>;
  /** Resolves with a failure flag; on success the server action redirects away. */
  remove: (password: string) => Promise<{ failed: boolean } | void>;
};

export function AccountActions({ actions, hasPassword, consentPending }: { actions: AccountActionHandlers; hasPassword: boolean; consentPending: boolean }) {
  const [error, setError] = useState<string | undefined>();
  const [pending, setPending] = useState(false);

  async function remove(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (pending) return;
    const data = new FormData(e.currentTarget);
    if (data.get("confirm") !== "on") return setError(at("account.deleteConfirmRequired"));
    setPending(true);
    setError(undefined);
    try {
      const r = await actions.remove(String(data.get("password") ?? ""));
      if (r && r.failed) setError(at("account.deleteFailed"));
    } finally {
      setPending(false);
    }
  }

  return (
    <>
      {consentPending ? (
        <form action={actions.accept}>
          <Button type="submit">{at("account.acceptCurrent")}</Button>
        </form>
      ) : null}
      <Card>
        <h2>{at("account.deleteTitle")}</h2>
        <p>{at("account.deleteBody")}</p>
        <form onSubmit={remove} noValidate>
          {error ? <Alert variant="danger">{error}</Alert> : null}
          {hasPassword ? (
            <Field label={at("account.deletePassword")} required>
              {(c) => <TextInput {...c} name="password" type="password" autoComplete="current-password" />}
            </Field>
          ) : null}
          <label>
            <input type="checkbox" name="confirm" /> {at("account.deleteConfirm")}
          </label>
          <Button type="submit" variant="danger" loading={pending}>{at("account.deleteSubmit")}</Button>
        </form>
      </Card>
    </>
  );
}
