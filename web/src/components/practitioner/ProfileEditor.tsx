"use client";
import { useState, type FormEvent } from "react";
import { Alert, Button, Field, TextArea, TextInput } from "@/components/ui";
import { createApi, type PractitionerApi, type PractitionerPrivate, type ProfileInput } from "./api";
import { CountedInput } from "./CountedInput";
import { fieldErrorsFrom, generalMessage } from "./fieldErrors";
import { pp } from "./strings";

const blankToUndefined = (s: string) => (s.trim() === "" ? undefined : s.trim());

export function ProfileEditor({ initial, api }: { initial: PractitionerPrivate; api?: PractitionerApi }) {
  const [saved, setSaved] = useState(initial);
  const [v, setV] = useState({
    displayName: initial.displayName,
    headline: initial.headline,
    bio: initial.bio,
    portfolioUrl: initial.portfolioUrl ?? "",
    meetingUrlDefault: initial.meetingUrlDefault ?? "",
    nip: initial.nip ?? "",
  });
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [general, setGeneral] = useState<string>();
  const [done, setDone] = useState(false);
  const [pending, setPending] = useState(false);
  const set = (k: keyof typeof v) => (e: { target: { value: string } }) => {
    setV((x) => ({ ...x, [k]: e.target.value }));
    setDone(false);
  };

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (pending) return;
    const input: ProfileInput = {
      displayName: v.displayName.trim(),
      headline: v.headline.trim(),
      bio: v.bio.trim(),
      portfolioUrl: blankToUndefined(v.portfolioUrl),
      meetingUrlDefault: blankToUndefined(v.meetingUrlDefault),
      nip: blankToUndefined(v.nip),
    };
    // Empty optional fields are omitted, not sent as null (CONTRACTS 0).
    for (const k of ["portfolioUrl", "meetingUrlDefault", "nip"] as const) if (input[k] === undefined) delete input[k];
    setPending(true);
    setErrors({});
    setGeneral(undefined);
    setDone(false);
    const r = await (api ?? createApi()).saveProfile(input);
    setPending(false);
    if (r.ok) {
      setSaved((s) => ({ ...s, ...(r.value ?? {}) }));
      return setDone(true);
    }
    const fields = fieldErrorsFrom(r.problem, { headline: v.headline, bio: v.bio });
    setErrors(fields);
    if (Object.keys(fields).length === 0) setGeneral(generalMessage(r.problem, pp("common.unavailable")));
  }

  const nipChanges = saved.status === "Approved" && (blankToUndefined(v.nip) ?? "") !== (saved.nip ?? "");
  return (
    <>
      <h1>{pp("profile.title")}</h1>
      <form onSubmit={submit} noValidate>
        {general ? <Alert variant="danger">{general}</Alert> : null}
        {done ? <Alert variant="success">{pp("common.saved")}</Alert> : null}
        <Field label={pp("profile.displayName")} error={errors.displayName} required>
          {(c) => <CountedInput {...c} name="displayName" value={v.displayName} maxLength={80} onChange={set("displayName")} />}
        </Field>
        <Field label={pp("profile.headline")} hint={pp("profile.headlineHint")} error={errors.headline} required>
          {(c) => <CountedInput {...c} name="headline" value={v.headline} maxLength={120} onChange={set("headline")} />}
        </Field>
        <Field label={pp("profile.bio")} hint={pp("profile.bioHint")} error={errors.bio}>
          {(c) => <TextArea {...c} name="bio" rows={8} maxLength={2000} showCount value={v.bio} onChange={set("bio")} />}
        </Field>
        <Field label={pp("profile.portfolio")} hint={pp("profile.portfolioHint")} error={errors.portfolioUrl}>
          {(c) => <TextInput {...c} name="portfolioUrl" type="url" inputMode="url" maxLength={300} value={v.portfolioUrl} onChange={set("portfolioUrl")} />}
        </Field>
        <Field label={pp("profile.meeting")} hint={pp("profile.meetingHint")} error={errors.meetingUrlDefault}>
          {(c) => <TextInput {...c} name="meetingUrlDefault" type="url" inputMode="url" maxLength={300} value={v.meetingUrlDefault} onChange={set("meetingUrlDefault")} />}
        </Field>
        <Field label={pp("profile.nip")} hint={nipChanges ? pp("profile.nipChangeWarning") : pp("profile.nipHint")} error={errors.nip}>
          {(c) => <TextInput {...c} name="nip" inputMode="numeric" maxLength={10} value={v.nip} onChange={set("nip")} />}
        </Field>
        <Button type="submit" loading={pending}>{pending ? pp("common.saving") : pp("common.save")}</Button>
      </form>
    </>
  );
}
