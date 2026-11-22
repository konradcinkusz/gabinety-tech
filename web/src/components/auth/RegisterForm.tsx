"use client";
import { useState, type FormEvent } from "react";
import { Alert, Button, Field, TextInput } from "@/components/ui";
import type { Navigate } from "./LoginForm";
import { safeReturnTo } from "./returnTo";
import { at } from "./strings";

const defaultNavigate: Navigate = (to) => window.location.assign(to);

/** `versions` are the consent versions the identity provider currently requires; they are sent back verbatim. */
export function RegisterForm({
  versions,
  returnTo,
  navigate = defaultNavigate,
}: {
  versions: { terms: string; privacy: string };
  returnTo: string;
  navigate?: Navigate;
}) {
  const [error, setError] = useState<string | undefined>();
  const [sent, setSent] = useState(false);
  const [pending, setPending] = useState(false);

  async function submit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (pending) return;
    const data = new FormData(e.currentTarget);
    if (data.get("consent") !== "on") return setError(at("register.consentRequired"));
    const password = String(data.get("password") ?? "");
    if (password.length < 8) return setError(at("register.passwordTooShort"));
    // authservice's password rules (ASP.NET Identity options, Program.cs): a digit, a lower-case and an upper-case letter and a non-alphanumeric character.
    if (!/\d/.test(password) || !/\p{Ll}/u.test(password) || !/\p{Lu}/u.test(password) || !/[^\p{L}\p{N}]/u.test(password)) return setError(at("register.passwordWeak"));
    setPending(true);
    setError(undefined);
    try {
      const res = await fetch("/api/auth/register", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          email: data.get("email"),
          password,
          acceptedTermsVersion: versions.terms,
          acceptedPrivacyVersion: versions.privacy,
          locale: "pl",
        }),
      });
      if (res.status === 202) return setSent(true);
      if (res.ok) return navigate(safeReturnTo(returnTo));
      setError(res.status === 429 ? at("register.rateLimited") : res.status >= 500 ? at("register.unavailable") : at("register.failed"));
    } catch {
      setError(at("register.unavailable"));
    } finally {
      setPending(false);
    }
  }

  if (sent) return <Alert variant="success">{at("register.checkInbox")}</Alert>;
  return (
    <form onSubmit={submit} noValidate>
      {error ? <Alert variant="danger">{error}</Alert> : null}
      <Field label={at("register.email")} required>
        {(c) => <TextInput {...c} name="email" type="email" autoComplete="email" />}
      </Field>
      <Field label={at("register.password")} hint={at("register.passwordHint")} required>
        {(c) => <TextInput {...c} name="password" type="password" autoComplete="new-password" />}
      </Field>
      <label>
        <input type="checkbox" name="consent" />{" "}
        {at("register.consent", { terms: versions.terms, privacy: versions.privacy })}
      </label>
      <Button type="submit" loading={pending}>{at("register.submit")}</Button>
    </form>
  );
}
