"use client";
import { useState } from "react";
import { Alert, Button, Card, Field, Select, TextArea } from "@/components/ui";
import type { BookingDetailView, BookingsApi, DisputeKind, Failure, Outcome } from "./api";
import styles from "./bookings.module.css";
import { DISPUTE_MAX, DISPUTE_MIN, failureKey, fieldMessages, REASON_MAX, toSignIn } from "./lib";
import type { Role } from "./routes";
import { mb } from "./strings";

type Common = { api: BookingsApi; bookingId: string; onDone: (b: BookingDetailView, message: string) => void; onClose: () => void; onRefresh: () => void };

/** Runs one action: a 401 goes to sign-in, a success reports the new booking, a failure is kept for display. */
function useAction(onDone: Common["onDone"], doneMessage: string) {
  const [pending, setPending] = useState(false);
  const [failure, setFailure] = useState<Failure | null>(null);
  async function run(call: () => Promise<Outcome<BookingDetailView>>) {
    setPending(true);
    setFailure(null);
    const r = await call();
    setPending(false);
    if (r.ok) return onDone(r.value, doneMessage);
    if (r.status === 401) return toSignIn();
    setFailure(r);
  }
  return { pending, failure, run };
}

function Failed({ failure, onRefresh }: { failure: Failure | null; onRefresh: () => void }) {
  if (!failure) return null;
  return (
    <Alert variant="danger">
      {mb(failureKey(failure))}
      {failure.code === "invalid_transition" && <> <Button variant="secondary" onClick={onRefresh}>{mb("error.refresh")}</Button></>}
    </Alert>
  );
}

/** The consequence for the money is stated BEFORE the confirm button, from the rules (CONTRACTS 9.2); the client's clock decides nothing. */
export function CancelPanel({ role, ...c }: Common & { role: Role }) {
  const [reason, setReason] = useState("");
  const { pending, failure, run } = useAction(c.onDone, mb("cancel.done"));
  const errors = failure ? fieldMessages(failure) : {};
  return (
    <Card>
      <section role="alertdialog" aria-labelledby="cancel-title" className={styles.panel}>
        <h2 id="cancel-title">{mb("cancel.title")}</h2>
        <p>{mb(role === "practitioner" ? "cancel.practitionerRule" : "cancel.clientRule")}</p>
        <Field label={mb("cancel.reasonLabel")} hint={mb("cancel.reasonHint")} error={errors.reason}>
          {(p) => <TextArea {...p} rows={3} maxLength={REASON_MAX} showCount value={reason} onChange={(e) => setReason(e.target.value)} />}
        </Field>
        <Failed failure={failure} onRefresh={c.onRefresh} />
        <div className={styles.actions}>
          <Button variant="danger" loading={pending} onClick={() => run(() => c.api.cancel(c.bookingId, reason.trim() || undefined))}>{mb("cancel.confirm")}</Button>
          <Button variant="secondary" onClick={c.onClose}>{mb("cancel.back")}</Button>
        </div>
      </section>
    </Card>
  );
}

const KINDS: DisputeKind[] = ["not_as_described", "practitioner_no_show", "other"];

export function DisputePanel(c: Common) {
  const [kind, setKind] = useState<DisputeKind>("not_as_described");
  const [text, setText] = useState("");
  const [local, setLocal] = useState<string | null>(null);
  const { pending, failure, run } = useAction(c.onDone, mb("dispute.done"));
  const errors = failure ? fieldMessages(failure) : {};
  function submit() {
    const n = text.trim().length;
    if (n < DISPUTE_MIN) return setLocal(mb("dispute.tooShort"));
    if (n > DISPUTE_MAX) return setLocal(mb("dispute.tooLong"));
    setLocal(null);
    void run(() => c.api.dispute(c.bookingId, kind, text.trim()));
  }
  return (
    <Card>
      <form className={styles.panel} onSubmit={(e) => { e.preventDefault(); submit(); }} noValidate>
        <h2>{mb("dispute.title")}</h2>
        <Field label={mb("dispute.kindLabel")} error={errors.kind}>
          {(p) => (
            <Select {...p} value={kind} onChange={(e) => setKind(e.target.value as DisputeKind)}>
              {KINDS.map((k) => <option key={k} value={k}>{mb(`dispute.kind_${k}`)}</option>)}
            </Select>
          )}
        </Field>
        <Field label={mb("dispute.textLabel")} hint={mb("dispute.textHint")} error={local ?? errors.text}>
          {(p) => <TextArea {...p} rows={5} showCount value={text} onChange={(e) => setText(e.target.value)} />}
        </Field>
        <Failed failure={failure} onRefresh={c.onRefresh} />
        <div className={styles.actions}>
          <Button type="submit" loading={pending}>{mb("dispute.submit")}</Button>
          <Button variant="secondary" onClick={c.onClose}>{mb("dispute.cancel")}</Button>
        </div>
      </form>
    </Card>
  );
}

export function NoShowPanel(c: Common) {
  const { pending, failure, run } = useAction(c.onDone, mb("noShow.done"));
  return (
    <Card>
      <section role="alertdialog" aria-labelledby="noshow-title" className={styles.panel}>
        <h2 id="noshow-title">{mb("noShow.title")}</h2>
        <p>{mb("noShow.body")}</p>
        <Failed failure={failure} onRefresh={c.onRefresh} />
        <div className={styles.actions}>
          <Button variant="danger" loading={pending} onClick={() => run(() => c.api.reportNoShow(c.bookingId))}>{mb("noShow.confirm")}</Button>
          <Button variant="secondary" onClick={c.onClose}>{mb("noShow.back")}</Button>
        </div>
      </section>
    </Card>
  );
}
