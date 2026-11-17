"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { Alert, Badge, Button, Card, Field, TextArea, TextInput, formatPln } from "@/components/ui";
import {
  amountProblem, fetchPrice, noteProblem, parsePlnToGrosz, runResolve, NOTE_MAX, RESOLUTIONS,
  type AdminDispute, type DisputeStatus, type Resolution, type ResolveResult,
} from "./api";
import { dv, type DvKey } from "./strings";

const warsaw = (iso: string) => new Date(iso).toLocaleString("pl-PL", { timeZone: "Europe/Warsaw" });
const known = (v: string | null | undefined, set: readonly string[]) => (v && set.includes(v) ? v : null);
const KINDS = ["not_as_described", "practitioner_no_show", "other"] as const;

function errorKey(r: Extract<ResolveResult, { ok: false }>): DvKey {
  if (r.status === 409) return "error.stateChanged";
  if (r.status === 403) return "error.forbidden";
  if (r.status === 404) return "error.notFound";
  if (r.status === 0) return "error.network";
  if (r.status === 400 || r.status === 422) return r.fields.includes("refundMinor") ? "error.refundMinor" : r.fields.includes("note") ? "error.note" : "error.validation";
  if (r.status === 503) return "error.unavailable";
  return "error.generic";
}

function ResolvePanel({ bookingId }: { bookingId: string }) {
  const router = useRouter();
  const [open, setOpen] = useState(false);
  const [price, setPrice] = useState<number | null>(null);
  const [resolution, setResolution] = useState<Resolution | null>(null);
  const [amount, setAmount] = useState("");
  const [note, setNote] = useState("");
  const [step, setStep] = useState<"edit" | "confirm">("edit");
  const [problem, setProblem] = useState<DvKey | null>(null);
  const [error, setError] = useState<DvKey | null>(null);
  const [busy, setBusy] = useState(false);

  async function show() {
    setOpen(true);
    setPrice(await fetchPrice(bookingId)); // for the 1..price-1 range and the words of the confirmation; null: the server decides
  }

  /** The refund in grosz when the form is valid; sets `problem` and returns undefined otherwise. */
  function validate(): { refund?: number } | undefined {
    if (!resolution) return void setProblem("resolve.pick");
    if (resolution === "refund_partial") {
      const p = parsePlnToGrosz(amount);
      if (!p.ok) return void setProblem(p.problem === "decimals" ? "resolve.amountDecimals" : "resolve.amountFormat");
      if (amountProblem(p.grosz, price)) return void setProblem("resolve.amountRange");
      if (noteProblem(note)) return void setProblem("resolve.noteInvalid");
      return { refund: p.grosz };
    }
    if (noteProblem(note)) return void setProblem("resolve.noteInvalid");
    return {};
  }

  function review() {
    setProblem(null);
    setError(null);
    if (validate()) setStep("confirm");
  }

  async function submit() {
    const v = validate();
    if (!v || !resolution) return setStep("edit");
    setBusy(true);
    setError(null);
    const r = await runResolve(bookingId, { resolution, ...(v.refund !== undefined ? { refundMinor: v.refund } : {}), note });
    setBusy(false);
    if (r.ok) return router.refresh(); // the server's list is the truth: the resolved dispute leaves it
    setError(errorKey(r));
    if (r.status === 409) router.refresh();
  }

  if (!open) return <Button onClick={show}>{dv("resolve.open")}</Button>;

  const refund = resolution === "refund_partial" ? parsePlnToGrosz(amount) : null;
  const refundGrosz = refund?.ok ? refund.grosz : 0;
  // Only built on the confirm step: by then the amount passed amountProblem, so price - refund is positive.
  const confirmText = step !== "confirm" ? "" :
    resolution === "refund_partial"
      ? dv(price === null ? "confirm.partialNoPrice" : "confirm.partial", { refund: formatPln(refundGrosz), kept: price === null ? "" : formatPln(price - refundGrosz) })
      : resolution
        ? dv(price === null ? (`confirm.${resolution}NoPrice` as const) : (`confirm.${resolution}` as const), { price: price === null ? "" : formatPln(price) })
        : "";

  return (
    <form onSubmit={(e) => e.preventDefault()}>
      {step === "edit" ? (
        <>
          <fieldset>
            <legend>{dv("resolve.legend")}</legend>
            {RESOLUTIONS.map((r) => (
              <label key={r}>
                <input type="radio" name={`resolution-${bookingId}`} value={r} checked={resolution === r} onChange={() => (setResolution(r), setProblem(null))} />
                {dv(`resolve.${r}`)}
              </label>
            ))}
          </fieldset>
          {resolution === "refund_partial" ? (
            <Field label={dv("resolve.amount")} hint={dv("resolve.amountHint")} required>
              {(c) => <TextInput {...c} name="amount" inputMode="decimal" autoComplete="off" value={amount} onChange={(e) => setAmount(e.target.value)} />}
            </Field>
          ) : null}
          <Field label={dv("resolve.note")} required>
            {(c) => <TextArea {...c} value={note} maxLength={NOTE_MAX} showCount rows={3} onChange={(e) => setNote(e.target.value)} />}
          </Field>
          {problem ? <Alert variant="danger">{problem === "resolve.amountRange" && price !== null ? dv("resolve.amountRangeWith", { price: formatPln(price) }) : dv(problem)}</Alert> : null}
          <Button onClick={review}>{dv("resolve.review")}</Button>{" "}
          <Button variant="secondary" onClick={() => setOpen(false)}>{dv("resolve.cancel")}</Button>
        </>
      ) : (
        <>
          <Alert variant="warning">
            <strong>{dv("resolve.confirmTitle")}</strong> {confirmText}
          </Alert>
          {error ? <Alert variant={error === "error.stateChanged" ? "warning" : "danger"}>{dv(error)}</Alert> : null}
          <Button variant="danger" loading={busy} onClick={submit}>{dv("resolve.confirm")}</Button>{" "}
          <Button variant="secondary" disabled={busy} onClick={() => (setStep("edit"), setError(null))}>{dv("resolve.back")}</Button>
        </>
      )}
    </form>
  );
}

function Row({ d, mode }: { d: AdminDispute; mode: DisputeStatus }) {
  const kind = known(d.kind, KINDS) ?? "other";
  const outcome = known(d.resolution, RESOLUTIONS);
  return (
    <li>
      <Card>
        <h2>{dv(`kind.${kind}` as DvKey)}</h2>
        {mode === "resolved" && outcome ? <Badge>{dv(`resolution.${outcome}` as DvKey)}</Badge> : null}
        <dl>
          <dt>{dv("fact.booking")}</dt>
          <dd>{d.bookingId}</dd>
          <dt>{dv("fact.client")}</dt>
          <dd>{d.clientUserId}</dd>
          <dt>{dv("fact.practitioner")}</dt>
          <dd>{d.practitionerUserId}</dd>
          {d.openedAt ? (
            <>
              <dt>{dv("fact.opened")}</dt>
              <dd><time dateTime={d.openedAt}>{warsaw(d.openedAt)}</time></dd>
            </>
          ) : null}
          {mode === "resolved" && d.resolvedAt ? (
            <>
              <dt>{dv("fact.resolved")}</dt>
              <dd><time dateTime={d.resolvedAt}>{warsaw(d.resolvedAt)}</time></dd>
            </>
          ) : null}
          <dt>{dv("fact.text")}</dt>
          <dd style={{ whiteSpace: "pre-wrap" }}>{d.text ?? ""}</dd>
          {mode === "resolved" && d.note ? (
            <>
              <dt>{dv("fact.note")}</dt>
              <dd style={{ whiteSpace: "pre-wrap" }}>{d.note}</dd>
            </>
          ) : null}
        </dl>
        {mode === "open" ? <ResolvePanel bookingId={d.bookingId} /> : null}
      </Card>
    </li>
  );
}

export function DisputeList({ items, mode }: { items: AdminDispute[]; mode: DisputeStatus }) {
  if (items.length === 0) return <p>{dv(`empty.${mode}` as DvKey)}</p>;
  return (
    <ul>
      {items.map((d) => (
        <Row key={d.bookingId} d={d} mode={mode} />
      ))}
    </ul>
  );
}
