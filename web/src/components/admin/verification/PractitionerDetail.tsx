import Link from "next/link";
import { Alert, Badge, Card } from "@/components/ui";
import { EVENT_KINDS, type AdminPractitionerDetail, type AdminVerificationEvent } from "./api";
import { av, type AvKey } from "./strings";

const when = (iso: string) => (
  <time dateTime={iso}>{new Date(iso).toLocaleString("pl-PL", { timeZone: "Europe/Warsaw" })}</time>
);
const eventKey = (k: string): AvKey => ((EVENT_KINDS as readonly string[]).includes(k) ? `event.${k}` : "event.unknown") as AvKey; // never the raw string
const isHttp = (u: string) => /^https?:\/\//i.test(u);

/** Server-renderable: text only (bio, reason and notes are free text and are rendered as text nodes). `events` null = the history could not be loaded. */
export function PractitionerDetail({ detail: d, events }: { detail: AdminPractitionerDetail; events: AdminVerificationEvent[] | null }) {
  const portfolio = d.portfolioUrl ?? null;
  return (
    <>
      <p><Link href="/admin/weryfikacja">{av("detail.back")}</Link></p>
      <h1>{d.displayName}</h1>
      <p>{d.headline}</p>
      <Badge>{av(`status.${d.status}` as AvKey)}</Badge>
      <Card>
        <h2>{av("detail.data")}</h2>
        <dl>
          <dt>{av("fact.slug")}</dt>
          <dd>{d.slug}</dd>
          <dt>{av("fact.github")}</dt>
          <dd>
            {d.githubLinked && d.githubLogin ? (
              <a href={`https://github.com/${encodeURIComponent(d.githubLogin)}`} rel="noopener noreferrer" target="_blank">{d.githubLogin}</a>
            ) : (
              av(d.githubLinked ? "fact.githubLinked" : "fact.githubNot")
            )}
          </dd>
          <dt>{av("fact.nip")}</dt>
          <dd>{d.nip ? `${d.nip} (${av(d.nipValid ? "fact.nipValid" : "fact.nipInvalid")})` : av("fact.none")}</dd>
          <dt>{av("fact.portfolio")}</dt>
          <dd>
            {portfolio && isHttp(portfolio) ? <a href={portfolio} rel="noopener noreferrer nofollow" target="_blank">{portfolio}</a> : portfolio ?? av("fact.none")}
          </dd>
          <dt>{av("fact.created")}</dt>
          <dd>{when(d.createdAt)}</dd>
          {d.submittedAt ? (<><dt>{av("fact.submitted")}</dt><dd>{when(d.submittedAt)}</dd></>) : null}
          {d.approvedAt ? (<><dt>{av("fact.approved")}</dt><dd>{when(d.approvedAt)}</dd></>) : null}
          <dt>{av("fact.updated")}</dt>
          <dd>{when(d.updatedAt)}</dd>
          {d.statusReason ? (<><dt>{av("fact.reason")}</dt><dd>{d.statusReason}</dd></>) : null}
        </dl>
        <h3>{av("fact.bio")}</h3>
        <p style={{ whiteSpace: "pre-wrap" }}>{d.bio}</p>
      </Card>
      <section aria-labelledby="timeline-title">
        <h2 id="timeline-title">{av("timeline.title")}</h2>
        {events === null ? (
          <Alert variant="warning">{av("timeline.unavailable")}</Alert>
        ) : events.length === 0 ? (
          <p>{av("timeline.empty")}</p>
        ) : (
          <ol>
            {events.map((e) => (
              <li key={e.id}>
                <strong>{av(eventKey(e.kind))}</strong> · {when(e.at)}
                <br />
                <small>{av("timeline.actor")}: {e.actorUserId}</small>
                {e.note ? <p>{av("timeline.note")}: {e.note}</p> : null}
              </li>
            ))}
          </ol>
        )}
      </section>
    </>
  );
}
