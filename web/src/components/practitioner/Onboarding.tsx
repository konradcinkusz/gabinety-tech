"use client";
import Link from "next/link";
import { useState } from "react";
import { Alert, Badge, Button, Card, Field, TextInput } from "@/components/ui";
import { createApi, type OfferingPrivate, type PractitionerApi, type PractitionerPrivate } from "./api";
import { buildChecklist, type ChecklistKey } from "./checklist";
import { generalMessage } from "./fieldErrors";
import { pp } from "./strings";

const WHERE: Record<ChecklistKey, string> = {
  headline: "profil", bio: "profil", offering: "oferty", github: "", nip: "profil", portfolio: "profil", email: "",
};
const UNMET: Record<string, ChecklistKey> = {
  headline: "headline", bio: "bio", offering: "offering", github_linked: "github", nip_valid: "nip", portfolioUrl: "portfolio", email_verified: "email",
};
const STATUS_VARIANT = { Draft: "neutral", Submitted: "warning", Approved: "success", Rejected: "danger", Suspended: "danger" } as const;
const GITHUB_LOGIN = /^[A-Za-z0-9-]{1,39}$/;

type Note = { variant: "success" | "danger" | "info"; text: string };

/** Everything on this page is read back from the API after each action: the checklist is never ticked from local state. */
export function Onboarding({ initialProfile, initialOfferings, api }: { initialProfile: PractitionerPrivate; initialOfferings: OfferingPrivate[]; api?: PractitionerApi }) {
  const client = api ?? createApi();
  const [profile, setProfile] = useState(initialProfile);
  const [offerings, setOfferings] = useState(initialOfferings);
  const [note, setNote] = useState<Note>();
  const [login, setLogin] = useState(initialProfile.githubLogin ?? "");
  const [loginError, setLoginError] = useState<string>();
  const [challenge, setChallenge] = useState<string>();
  const [githubNote, setGithubNote] = useState<string>();
  const [busy, setBusy] = useState<"submit" | "github" | undefined>();

  async function refresh(): Promise<boolean> {
    const [p, o] = await Promise.all([client.getProfile(), client.listOfferings()]);
    if (p.ok) setProfile(p.value);
    if (o.ok) setOfferings(o.value);
    return p.ok && o.ok;
  }

  async function requestCode() {
    if (!GITHUB_LOGIN.test(login.trim())) return setLoginError(pp("github.invalidLogin"));
    setLoginError(undefined);
    setBusy("github");
    const r = await client.requestGithubLink(login.trim());
    setBusy(undefined);
    if (r.ok) { setChallenge(r.value.code); setGithubNote(undefined); }
    else setGithubNote(r.problem.status === 503 || r.problem.status === 0 ? generalMessage(r.problem, "") : r.problem.errors.githubLogin ? pp("github.invalidLogin") : pp("github.failed"));
  }

  async function verify() {
    setBusy("github");
    const r = await client.verifyGithubLink();
    setGithubNote(r.ok ? undefined : r.problem.code === "github_not_linked" ? pp("github.notLinked") : r.problem.status === 503 ? pp("github.unavailable") : pp("github.failed"));
    if (r.ok) await refresh();
    setBusy(undefined);
  }

  async function submit() {
    setBusy("submit");
    setNote(undefined);
    const r = await client.submit();
    if (r.ok) {
      setNote((await refresh()) ? { variant: "success", text: pp("onboarding.submitted") } : { variant: "info", text: pp("onboarding.refreshFailed") });
    } else if (r.problem.code === "submit_incomplete") {
      const items = r.problem.unmetItems.map((i) => (UNMET[i] ? pp(`onboarding.items.${UNMET[i]}`) : i)).join(", ");
      setNote({ variant: "danger", text: pp("onboarding.incomplete", { items }) });
      await refresh();
    } else {
      setNote({ variant: "danger", text: r.problem.code === "invalid_transition" ? pp("onboarding.invalidTransition") : generalMessage(r.problem, pp("onboarding.submitFailed")) });
    }
    setBusy(undefined);
  }

  const items = buildChecklist(profile, offerings);
  const canSubmit = profile.status === "Draft" || profile.status === "Rejected";
  return (
    <>
      <h1>{pp("onboarding.title")}</h1>
      <Card>
        <p>
          {pp("status.label")}: <Badge variant={STATUS_VARIANT[profile.status]}>{pp(`status.${profile.status}`)}</Badge>
        </p>
        {profile.statusReason ? <p>{pp("status.reason", { reason: profile.statusReason })}</p> : null}
        {profile.status === "Rejected" ? <p>{pp("status.rejectedHelp")}</p> : null}
      </Card>
      <h2>{pp("onboarding.checklist")}</h2>
      <ol>
        {items.map(({ key, done }) => (
          <li key={key} data-item={key} data-done={done}>
            <strong>{pp(`onboarding.items.${key}`)}</strong>: {done ? pp("onboarding.done") : pp("onboarding.todo")}
            {done ? null : (
              <>
                {" "}
                {WHERE[key] ? <Link href={`/gabinet-programisty/${WHERE[key]}`}>{pp(`onboarding.itemHelp.${key}`)}</Link> : <span>{pp(`onboarding.itemHelp.${key}`)}</span>}
              </>
            )}
          </li>
        ))}
      </ol>
      {note ? <Alert variant={note.variant}>{note.text}</Alert> : null}
      {canSubmit ? <Button onClick={submit} loading={busy === "submit"}>{busy === "submit" ? pp("onboarding.submitting") : pp("onboarding.submit")}</Button> : null}
      <Card>
        <h2>{pp("github.title")}</h2>
        {profile.githubLinked ? (
          <p>{pp("github.linked", { login: profile.githubLogin ?? "" })}</p>
        ) : (
          <>
            <p>{pp("github.help")}</p>
            <Field label={pp("github.login")} error={loginError}>
              {(c) => <TextInput {...c} name="githubLogin" maxLength={39} value={login} onChange={(e) => setLogin(e.target.value)} autoComplete="off" />}
            </Field>
            <Button variant="secondary" onClick={requestCode} loading={busy === "github"}>{pp("github.request")}</Button>
            {challenge ? (
              <>
                <p>{pp("github.codeIntro")}</p>
                <p><code>{challenge}</code></p>
                <Button onClick={verify} loading={busy === "github"}>{pp("github.verify")}</Button>
              </>
            ) : null}
            {githubNote ? <Alert variant="danger">{githubNote}</Alert> : null}
          </>
        )}
      </Card>
    </>
  );
}
