"use client";
import { useState, type FormEvent } from "react";
import { Alert, Button, Field, LinkButton, TextInput } from "@/components/ui";
import { RETURN_PARAM, safeReturnTo } from "./returnTo";
import { at } from "./strings";

export type Navigate = (to: string) => void;
const defaultNavigate: Navigate = (to) => window.location.assign(to);

export function GithubButton({ returnTo }: { returnTo: string }) {
  const back = safeReturnTo(returnTo);
  const href = "/api/auth/github/start" + (back === "/" ? "" : "?" + RETURN_PARAM + "=" + encodeURIComponent(back));
  return <LinkButton href={href} variant="secondary">{at("login.github")}</LinkButton>;
}

/** Posts to the BFF only. Every failed sign-in prints one text, so the page never says whether an e-mail exists. */
export function LoginForm({ returnTo, notice, navigate = defaultNavigate }: { returnTo: string; notice?: string; navigate?: Navigate }) {
  const [error, setError] = useState<string | undefined>(notice);
  const [pending, setPending] = useState(false);

  async function submit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (pending) return;
    const data = new FormData(e.currentTarget);
    setPending(true);
    setError(undefined);
    try {
      const res = await fetch("/api/auth/login", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ email: data.get("email"), password: data.get("password") }),
      });
      if (res.ok) return navigate(safeReturnTo(returnTo));
      const code = ((await res.json().catch(() => null)) as { code?: string } | null)?.code;
      setError(
        code === "email_verification_required" ? at("login.verificationRequired")
        : code === "two_factor_not_supported" ? at("login.twoFactor")
        : res.status === 429 ? at("login.rateLimited")
        : res.status === 401 ? at("login.invalidCredentials")
        : at("login.unavailable"),
      );
    } catch {
      setError(at("login.unavailable"));
    } finally {
      setPending(false);
    }
  }

  return (
    <form onSubmit={submit} noValidate>
      {error ? <Alert variant="danger">{error}</Alert> : null}
      <Field label={at("login.email")} required>
        {(c) => <TextInput {...c} name="email" type="email" autoComplete="email" />}
      </Field>
      <Field label={at("login.password")} required>
        {(c) => <TextInput {...c} name="password" type="password" autoComplete="current-password" />}
      </Field>
      <Button type="submit" loading={pending}>{pending ? at("login.submitting") : at("login.submit")}</Button>
      <p>{at("login.or")}</p>
      <GithubButton returnTo={returnTo} />
    </form>
  );
}
