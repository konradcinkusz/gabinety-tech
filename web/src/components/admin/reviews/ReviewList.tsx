"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { Alert, Badge, Button, Card, Field, TextArea } from "@/components/ui";
import { REASONS, reasonProblem, runDecision, REASON_MAX, type AdminReview, type DecisionResult, type ReviewStatus } from "./api";
import { mv, type MvKey } from "./strings";

const warsaw = (iso: string) => new Date(iso).toLocaleString("pl-PL", { timeZone: "Europe/Warsaw" });
const TRIAGE = ["none", "hold", "unavailable"] as const;
const reasonKey = (r: string): MvKey => ((REASONS as readonly string[]).includes(r) ? `reason.${r}` : "reason.other") as MvKey; // never the raw string
const triageKey = (f: string | null | undefined): MvKey => ((TRIAGE as readonly string[]).includes(f ?? "") ? `triage.${f}` : "triage.unknown") as MvKey;
const variantOf = (s: ReviewStatus) => (s === "Published" ? "success" : s === "Rejected" ? "danger" : "warning");

function errorKey(r: Extract<DecisionResult, { ok: false }>): MvKey {
  if (r.status === 409) return "error.stateChanged";
  if (r.status === 403) return "error.forbidden";
  if (r.status === 404) return "error.notFound";
  if (r.status === 0) return "error.network";
  if (r.status === 400 || r.status === 422) return "error.reason";
  if (r.status === 503) return "error.unavailable";
  return "error.generic";
}

function Decisions({ id, status }: { id: string; status: ReviewStatus }) {
  const router = useRouter();
  const [rejecting, setRejecting] = useState(false);
  const [reason, setReason] = useState("");
  const [problem, setProblem] = useState(false);
  const [error, setError] = useState<MvKey | null>(null);
  const [busy, setBusy] = useState(false);

  async function decide(action: "publish" | "reject") {
    if (action === "reject" && reasonProblem(reason)) return setProblem(true);
    setProblem(false);
    setError(null);
    setBusy(true);
    const r = await runDecision(id, action, reason);
    setBusy(false);
    if (r.ok) return router.refresh(); // the server's list is the truth: the moderated review leaves it
    setError(errorKey(r));
    if (r.status === 409) router.refresh();
  }

  return (
    <div>
      {rejecting ? (
        <form noValidate onSubmit={(e) => (e.preventDefault(), void decide("reject"))}>
          <Alert variant="warning">{mv("reject.warning")}</Alert>
          <Field label={mv("reject.label")} required error={problem ? mv("reject.reasonInvalid") : undefined}>
            {(c) => <TextArea {...c} value={reason} maxLength={REASON_MAX} showCount rows={3} onChange={(e) => setReason(e.target.value)} />}
          </Field>
          <Button type="submit" variant="danger" loading={busy}>{mv("reject.confirm")}</Button>{" "}
          <Button variant="secondary" disabled={busy} onClick={() => (setRejecting(false), setProblem(false), setError(null))}>{mv("reject.cancel")}</Button>
        </form>
      ) : (
        <>
          {status === "Held" ? <Button loading={busy} onClick={() => decide("publish")}>{mv("publish.open")}</Button> : null}{" "}
          <Button variant="danger" disabled={busy} onClick={() => setRejecting(true)}>{mv("reject.open")}</Button>
        </>
      )}
      {error ? <Alert variant={error === "error.stateChanged" ? "warning" : "danger"}>{mv(error)}</Alert> : null}
    </div>
  );
}

function Row({ r, mode }: { r: AdminReview; mode: ReviewStatus }) {
  const reasons = r.holdReasons ?? [];
  return (
    <li>
      <Card>
        <h2>{mv("fact.rating")}: {mv("ratingOf", { n: String(r.rating) })}</h2>
        <Badge variant={variantOf(r.status)}>{mv(`status.${r.status}` as MvKey)}</Badge>
        <dl>
          {r.createdAt ? (
            <>
              <dt>{mv("fact.created")}</dt>
              <dd><time dateTime={r.createdAt}>{warsaw(r.createdAt)}</time></dd>
            </>
          ) : null}
          {mode === "Published" && r.publishedAt ? (
            <>
              <dt>{mv("fact.published")}</dt>
              <dd><time dateTime={r.publishedAt}>{warsaw(r.publishedAt)}</time></dd>
            </>
          ) : null}
          <dt>{mv("fact.practitioner")}</dt>
          <dd>{r.practitionerId}</dd>
          <dt>{mv("fact.triage")}</dt>
          <dd>{mv(triageKey(r.triageFlag))}</dd>
          <dt>{mv("fact.reasons")}</dt>
          <dd>
            {reasons.length === 0 ? mv("reason.none") : (
              <ul>
                {reasons.map((x, i) => <li key={`${x}-${i}`}>{mv(reasonKey(x))}</li>)}
              </ul>
            )}
          </dd>
          <dt>{mv("fact.text")}</dt>
          <dd className="pre-wrap">{r.text ?? ""}</dd>
          {r.moderationNote ? (
            <>
              <dt>{mv("fact.note")}</dt>
              <dd className="pre-wrap">{r.moderationNote}</dd>
            </>
          ) : null}
          {r.moderatedBy ? (
            <>
              <dt>{mv("fact.moderator")}</dt>
              <dd>{r.moderatedBy}</dd>
            </>
          ) : null}
        </dl>
        {r.status !== "Rejected" ? <Decisions id={r.id} status={r.status} /> : null}
      </Card>
    </li>
  );
}

export function ReviewList({ items, mode }: { items: AdminReview[]; mode: ReviewStatus }) {
  if (items.length === 0) return <p>{mv(`empty.${mode}` as MvKey)}</p>;
  return (
    <ul>
      {items.map((r) => (
        <Row key={r.id} r={r} mode={mode} />
      ))}
    </ul>
  );
}
