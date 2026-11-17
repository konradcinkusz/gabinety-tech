"use client";
import { useState, type FormEvent } from "react";
import { Alert, Badge, Button, Card, Field, Select, TextArea, formatPln } from "@/components/ui";
import { createApi, type OfferingPrivate, type PractitionerApi, type Taxonomy } from "./api";
import { CountedInput } from "./CountedInput";
import { fieldErrorsFrom, generalMessage } from "./fieldErrors";
import { groszToInput, parsePricePln } from "./price";
import { pp } from "./strings";
import { TextInput } from "@/components/ui";

const MAX_ACTIVE = 5;
const MAX_TECHS = 4;
const empty = { title: "", description: "", duration: "30", price: "", problemSlug: "", techSlugs: [] as string[], meetingUrl: "" };
type Form = typeof empty;

export function OfferingsManager({ initial, taxonomy, api }: { initial: OfferingPrivate[]; taxonomy: Taxonomy | null; api?: PractitionerApi }) {
  const client = api ?? createApi();
  const [items, setItems] = useState(initial);
  const [editing, setEditing] = useState<string | null>(null);
  const [f, setF] = useState<Form>(empty);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [general, setGeneral] = useState<string>();
  const [done, setDone] = useState(false);
  const [pending, setPending] = useState(false);
  const active = items.filter((o) => o.isActive).length;
  const set = <K extends keyof Form>(k: K, v: Form[K]) => { setF((x) => ({ ...x, [k]: v })); setDone(false); };

  async function reload() {
    const r = await client.listOfferings();
    if (r.ok) setItems(r.value);
    return r.ok;
  }

  function edit(o: OfferingPrivate) {
    setEditing(o.id);
    setErrors({});
    setGeneral(undefined);
    setF({ title: o.title, description: o.description, duration: String(o.durationMinutes), price: groszToInput(o.price.amountMinor), problemSlug: o.problemSlug, techSlugs: o.techSlugs, meetingUrl: o.meetingUrl ?? "" });
  }

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (pending) return;
    setGeneral(undefined);
    setDone(false);
    const price = parsePricePln(f.price);
    if (!price.ok) return setErrors({ priceMinor: pp(`offerings.price_${price.reason}`) }); // refused here: the form never sends it
    setErrors({});
    setPending(true);
    const meeting = f.meetingUrl.trim();
    const r = await client.saveOffering(editing, {
      title: f.title.trim(),
      description: f.description.trim(),
      durationMinutes: f.duration === "60" ? 60 : 30,
      priceMinor: price.grosz,
      problemSlug: f.problemSlug,
      techSlugs: f.techSlugs,
      ...(meeting ? { meetingUrl: meeting } : {}),
    });
    if (r.ok) {
      const listed = await reload();
      setPending(false);
      setEditing(null);
      setF(empty);
      return listed ? setDone(true) : setGeneral(pp("common.unavailable"));
    }
    setPending(false);
    const fields = fieldErrorsFrom(r.problem, { title: f.title, description: f.description });
    setErrors(fields);
    if (r.problem.code === "too_many_offerings") setGeneral(pp("offerings.tooMany"));
    else if (Object.keys(fields).length === 0) setGeneral(generalMessage(r.problem, pp("offerings.failed")));
  }

  async function deactivate(id: string) {
    const r = await client.deactivateOffering(id);
    if (!r.ok) return setGeneral(generalMessage(r.problem, pp("offerings.failed")));
    if (editing === id) { setEditing(null); setF(empty); }
    await reload();
  }

  const toggleTech = (slug: string) => set("techSlugs", f.techSlugs.includes(slug) ? f.techSlugs.filter((s) => s !== slug) : [...f.techSlugs, slug]);
  return (
    <>
      <h1>{pp("offerings.title")}</h1>
      <p>{pp("offerings.activeCount", { n: active })}</p>
      {items.length === 0 ? <p>{pp("offerings.empty")}</p> : (
        <ul>
          {items.map((o) => (
            <li key={o.id}>
              <strong>{o.title}</strong> · {formatPln(o.price.amountMinor)} · {o.durationMinutes} min {o.isActive ? null : <Badge>{pp("offerings.inactive")}</Badge>}{" "}
              {o.isActive ? (
                <>
                  <Button variant="secondary" onClick={() => edit(o)}>{pp("offerings.edit")}</Button>{" "}
                  <Button variant="danger" onClick={() => deactivate(o.id)}>{pp("offerings.deactivate")}</Button>
                </>
              ) : null}
            </li>
          ))}
        </ul>
      )}
      <Card>
        <h2>{editing ? pp("offerings.formTitleEdit") : pp("offerings.formTitleNew")}</h2>
        {editing ? <p>{pp("offerings.priceChangeNote")}</p> : null}
        <form onSubmit={submit} noValidate>
          {general ? <Alert variant="danger">{general}</Alert> : null}
          {done ? <Alert variant="success">{pp("common.saved")}</Alert> : null}
          {taxonomy ? null : <Alert variant="danger">{pp("offerings.taxonomyUnavailable")}</Alert>}
          <Field label={pp("offerings.name")} hint={pp("offerings.nameHint")} error={errors.title} required>
            {(c) => <CountedInput {...c} name="title" value={f.title} maxLength={80} onChange={(e) => set("title", e.target.value)} />}
          </Field>
          <Field label={pp("offerings.description")} hint={pp("offerings.descriptionHint")} error={errors.description}>
            {(c) => <TextArea {...c} name="description" rows={5} maxLength={600} showCount value={f.description} onChange={(e) => set("description", e.target.value)} />}
          </Field>
          <Field label={pp("offerings.duration")} error={errors.durationMinutes} required>
            {(c) => (
              <Select {...c} name="duration" value={f.duration} onChange={(e) => set("duration", e.target.value)}>
                <option value="30">{pp("offerings.d30")}</option>
                <option value="60">{pp("offerings.d60")}</option>
              </Select>
            )}
          </Field>
          <Field label={pp("offerings.price")} hint={pp("offerings.priceHint")} error={errors.priceMinor} required>
            {(c) => <TextInput {...c} name="price" inputMode="decimal" autoComplete="off" value={f.price} onChange={(e) => set("price", e.target.value)} />}
          </Field>
          <Field label={pp("offerings.problem")} error={errors.problemSlug} required>
            {(c) => (
              <Select {...c} name="problemSlug" value={f.problemSlug} onChange={(e) => set("problemSlug", e.target.value)}>
                <option value="">{pp("offerings.choose")}</option>
                {(taxonomy?.problems ?? []).map((p) => <option key={p.slug} value={p.slug}>{p.namePl}</option>)}
              </Select>
            )}
          </Field>
          <fieldset>
            <legend>{pp("offerings.techs")}</legend>
            {(taxonomy?.techs ?? []).map((t) => (
              <label key={t.slug}>
                <input type="checkbox" checked={f.techSlugs.includes(t.slug)} disabled={!f.techSlugs.includes(t.slug) && f.techSlugs.length >= MAX_TECHS} onChange={() => toggleTech(t.slug)} /> {t.namePl}{" "}
              </label>
            ))}
            {errors.techSlugs ? <p role="alert">{errors.techSlugs}</p> : null}
          </fieldset>
          <Field label={pp("offerings.meeting")} hint={pp("offerings.meetingHint")} error={errors.meetingUrl}>
            {(c) => <TextInput {...c} name="meetingUrl" type="url" inputMode="url" maxLength={300} value={f.meetingUrl} onChange={(e) => set("meetingUrl", e.target.value)} />}
          </Field>
          <Button type="submit" loading={pending} disabled={!editing && active >= MAX_ACTIVE}>{pending ? pp("common.saving") : pp("common.save")}</Button>{" "}
          {editing ? <Button variant="secondary" onClick={() => { setEditing(null); setF(empty); setErrors({}); }}>{pp("offerings.cancel")}</Button> : null}
          {!editing && active >= MAX_ACTIVE ? <p>{pp("offerings.tooMany")}</p> : null}
        </form>
      </Card>
    </>
  );
}
