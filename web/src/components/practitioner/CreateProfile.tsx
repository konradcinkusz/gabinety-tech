"use client";
import { useState, type FormEvent } from "react";
import { Alert, Button, Field, TextInput } from "@/components/ui";
import { createApi, type PractitionerApi } from "./api";
import { pp } from "./strings";

/** The "create your gabinet" step for a signed-in user who has no profile yet. */
export function CreateProfile({ api, navigate = (to) => window.location.assign(to) }: { api?: PractitionerApi; navigate?: (to: string) => void }) {
  const [error, setError] = useState<string>();
  const [pending, setPending] = useState(false);

  async function submit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (pending) return;
    const name = String(new FormData(e.currentTarget).get("displayName") ?? "").trim();
    setPending(true);
    setError(undefined);
    const r = await (api ?? createApi()).createProfile(name);
    setPending(false);
    if (r.ok) return navigate("/gabinet-programisty");
    setError(r.problem.code === "practitioner_exists" ? pp("create.exists") : r.problem.status === 503 ? pp("common.unavailable") : pp("create.failed"));
  }

  return (
    <>
      <h1>{pp("create.title")}</h1>
      <p>{pp("create.intro")}</p>
      <form onSubmit={submit} noValidate>
        {error ? <Alert variant="danger">{error}</Alert> : null}
        <Field label={pp("create.displayName")} required>
          {(c) => <TextInput {...c} name="displayName" maxLength={80} autoComplete="name" />}
        </Field>
        <Button type="submit" loading={pending}>{pp("create.submit")}</Button>
      </form>
    </>
  );
}
