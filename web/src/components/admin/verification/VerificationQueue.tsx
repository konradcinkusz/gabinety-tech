"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { Alert, Badge, Button, Card, TextArea } from "@/components/ui";
import { allowedActions, needsReason, reasonProblem, runAction, REASON_MAX, type AdminAction, type AdminPractitionerRow } from "./api";
import { av, type AvKey } from "./strings";

function Row({ row }: { row: AdminPractitionerRow }) {
  const router = useRouter();
  const [asking, setAsking] = useState<AdminAction | null>(null);
  const [reason, setReason] = useState("");
  const [message, setMessage] = useState<AvKey | null>(null);
  const [busy, setBusy] = useState(false);

  async function perform(action: AdminAction) {
    if (needsReason(action) && reasonProblem(reason)) return setMessage("reason.invalid");
    setBusy(true);
    setMessage(null);
    const r = await runAction(row.id, action, needsReason(action) ? reason : undefined);
    setBusy(false);
    if (r.ok) {
      setAsking(null);
      setReason("");
      router.refresh(); // the server's status is the truth: reload, do not patch locally
    } else if (r.status === 409 && r.code === "invalid_transition") {
      setMessage("error.stateChanged");
      router.refresh();
    } else if (r.status === 403) setMessage("error.forbidden");
    else if (r.status === 404) setMessage("error.notFound");
    else setMessage(r.status === 0 ? "error.network" : "error.generic");
  }

  const submitted = row.submittedAt ?? null;
  const when = submitted ?? row.createdAt;
  return (
    <li>
      <Card>
        <h2>{row.displayName}</h2>
        <p>{row.headline}</p>
        <Badge>{av(`status.${row.status}` as AvKey)}</Badge>
        <dl>
          <dt>{av("fact.github")}</dt>
          <dd>
            {row.githubLinked && row.githubLogin ? (
              <a href={`https://github.com/${encodeURIComponent(row.githubLogin)}`} rel="noopener noreferrer" target="_blank">
                {row.githubLogin}
              </a>
            ) : (
              av(row.githubLinked ? "fact.githubLinked" : "fact.githubNot")
            )}
          </dd>
          <dt>{av("fact.nip")}</dt>
          <dd>{av(row.nipValid ? "fact.nipValid" : "fact.nipInvalid")}</dd>
          <dt>{av(submitted ? "fact.submitted" : "fact.created")}</dt>
          <dd>
            <time dateTime={when}>{new Date(when).toLocaleString("pl-PL", { timeZone: "Europe/Warsaw" })}</time>
          </dd>
          {row.statusReason ? (
            <>
              <dt>{av("fact.reason")}</dt>
              <dd>{row.statusReason}</dd>
            </>
          ) : null}
        </dl>
        {message ? <Alert variant={message === "error.stateChanged" ? "warning" : "danger"}>{av(message)}</Alert> : null}
        {asking ? (
          <div>
            <label>
              {av("reason.label")}
              <TextArea value={reason} maxLength={REASON_MAX} showCount rows={3} onChange={(e) => setReason(e.target.value)} />
            </label>
            <Button variant="danger" loading={busy} onClick={() => perform(asking)}>
              {av(asking === "reject" ? "action.confirmReject" : "action.confirmSuspend")}
            </Button>{" "}
            <Button variant="secondary" disabled={busy} onClick={() => { setAsking(null); setMessage(null); }}>
              {av("action.cancel")}
            </Button>
          </div>
        ) : (
          allowedActions(row.status).map((a) => (
            <Button
              key={a}
              variant={a === "approve" || a === "reinstate" ? "primary" : "danger"}
              loading={busy}
              onClick={() => (needsReason(a) ? (setAsking(a), setMessage(null)) : perform(a))}
            >
              {av(`action.${a}`)}
            </Button>
          ))
        )}
      </Card>
    </li>
  );
}

export function VerificationQueue({ items }: { items: AdminPractitionerRow[] }) {
  if (items.length === 0) return <p>{av("empty")}</p>;
  return (
    <ul>
      {items.map((r) => (
        <Row key={r.id} row={r} />
      ))}
    </ul>
  );
}
