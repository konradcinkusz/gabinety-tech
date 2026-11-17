"use client";
import { useRef, useState } from "react";
import { Alert, Button, Card, Field, LinkButton, TextArea } from "@/components/ui";
import { formatPln } from "@/components/ui/format";
import { dayHeading, warsawDate, warsawTime } from "@/components/slots/slotTime";
import { createBookingApi, type BookingApi, type CreateBookingInput, type Money } from "./api";
import { BriefAssistant } from "./BriefAssistant";
import styles from "./booking.module.css";
import { BRIEF_MAX, briefProblem, safeCheckoutTarget } from "./lib";
import { bk, type BookingTextKey } from "./strings";

export type BookingOffering = { id: string; title: string; durationMinutes: number; price: Money; problemSlug: string };
type Props = {
  offering: BookingOffering;
  practitioner: { slug: string; displayName: string };
  startUtc: string;
  api?: BookingApi;
  navigate?: (to: string) => void;
  newKey?: () => string;
};

const defaultApi = createBookingApi();
const defaultNavigate = (to: string) => window.location.assign(to);
const REASONS: Record<string, BookingTextKey> = {
  slot_taken: "error.slotTaken",
  slot_unavailable: "error.slotTaken",
  offering_inactive: "error.inactive",
  own_offering: "error.own",
  practitioner_not_bookable: "error.notBookable",
};

/**
 * One Idempotency-Key per attempt: it is made on the first submit, kept across retries of the SAME request (network error, 5xx,
 * double submit) and replaced as soon as the body differs, because the same key with another body is a 409 (CONTRACTS 0).
 */
export function BookingForm({ offering, practitioner, startUtc, api = defaultApi, navigate = defaultNavigate, newKey = () => crypto.randomUUID() }: Props) {
  const [text, setText] = useState("");
  const [applied, setApplied] = useState<string | null>(null); // the problem slug of the applied suggestion; null = none used
  const [pending, setPending] = useState(false);
  const [problem, setProblem] = useState<{ key: BookingTextKey; toProfile: boolean } | null>(null);
  const attempt = useRef<{ key: string; body: string } | null>(null);
  const inflight = useRef(false);
  const day = warsawDate(startUtc);
  const [fieldError, setFieldError] = useState<string | undefined>();

  async function pay() {
    if (inflight.current) return; // a state update is too late for a second click in the same tick
    const trimmed = text.trim();
    const p = briefProblem(trimmed);
    if (p) return setFieldError(bk(p === "short" ? "brief.tooShort" : "brief.tooLong"));
    setFieldError(undefined);
    const input: CreateBookingInput = { offeringId: offering.id, startUtc, brief: { text: trimmed, ...(applied ? { problemSlug: applied } : {}), aiAssisted: applied !== null } };
    const body = JSON.stringify(input);
    if (attempt.current?.body !== body) attempt.current = { key: newKey(), body };
    inflight.current = true;
    setPending(true);
    setProblem(null);
    const r = await api.create(attempt.current.key, input);
    inflight.current = false;
    if (r.ok && typeof r.value?.checkoutUrl === "string") {
      const to = safeCheckoutTarget(r.value.checkoutUrl, window.location.origin);
      if (to) return navigate(to); // stay disabled while the browser leaves
    }
    setPending(false);
    if (r.ok) return setProblem({ key: "error.generic", toProfile: false });
    if (r.status === 401) {
      return navigate("/logowanie?returnTo=" + encodeURIComponent(`/rezerwacja/${offering.id}?start=${encodeURIComponent(startUtc)}&gabinet=${practitioner.slug}`));
    }
    if (r.code === "idempotency_conflict") attempt.current = null;
    const reason = REASONS[r.code];
    setProblem(
      reason ? { key: reason, toProfile: true }
      : r.status === 429 ? { key: "error.rateLimited", toProfile: false }
      : r.status === 0 || r.status >= 500 ? { key: "error.unavailable", toProfile: false }
      : { key: "error.generic", toProfile: false },
    );
  }

  return (
    <div className={styles.layout}>
      <Card>
        <h2>{bk("summary.title")}</h2>
        <dl className={styles.summary}>
          <dt>{bk("summary.offering")}</dt><dd>{offering.title}</dd>
          <dt>{bk("summary.practitioner")}</dt><dd>{practitioner.displayName}</dd>
          <dt>{bk("summary.when")}</dt><dd>{dayHeading(day)}, {warsawTime(startUtc)}</dd>
          <dt>{bk("summary.duration")}</dt><dd>{bk("summary.minutes", { n: offering.durationMinutes })}</dd>
          <dt>{bk("summary.price")}</dt><dd className={styles.price}>{formatPln(offering.price.amountMinor)}</dd>
        </dl>
        <p className={styles.note}>{bk("summary.zone")}</p>
      </Card>
      <form onSubmit={(e) => { e.preventDefault(); void pay(); }} noValidate>
        <Field label={bk("brief.label")} hint={bk("brief.hint")} error={fieldError} required>
          {(c) => <TextArea {...c} name="brief" rows={8} maxLength={BRIEF_MAX} showCount value={text} onChange={(e) => setText(e.target.value)} />}
        </Field>
        <BriefAssistant text={text} disabled={pending} api={api} onApply={(proposal, slug) => { setText((t) => (t.trim() ? `${t.trimEnd()}\n\n${proposal}` : proposal)); setApplied(slug); }} />
        {problem && (
          <Alert variant="danger">
            {bk(problem.key)}{" "}
            {problem.toProfile && <LinkButton variant="secondary" href={`/gabinet/${practitioner.slug}`}>{bk("error.toProfile")}</LinkButton>}
          </Alert>
        )}
        <Button type="submit" loading={pending}>{pending ? bk("form.paying") : bk("form.pay")}</Button>
      </form>
    </div>
  );
}
