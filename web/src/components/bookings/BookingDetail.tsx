"use client";
import Link from "next/link";
import { useCallback, useEffect, useState } from "react";
import { Alert, Badge, Button, Card, LinkButton, Spinner } from "@/components/ui";
import { formatPln } from "@/components/ui/format";
import { safeHttpUrl } from "@/components/booking/lib";
import { CancelPanel, DisputePanel, NoShowPanel } from "./Actions";
import { createBookingsApi, type BookingDetailView, type BookingsApi, type Failure } from "./api";
import styles from "./bookings.module.css";
import { failureKey, otherParty, paymentLabel, statusLabel, toSignIn, whenLabel } from "./lib";
import { bookingsHref, reviewHref, type Role } from "./routes";
import { mb } from "./strings";

const defaultApi = createBookingsApi();
type Open = "cancel" | "dispute" | "noShow" | null;
type State = { kind: "loading" } | { kind: "error"; failure: Failure } | { kind: "ready"; booking: BookingDetailView };

/**
 * One booking. Every action button is driven ONLY by the server's `can*` flags and the meeting link only by `meetingUrl`: the windows
 * (24 h, 48 h, 72 h, 15 min) are never recomputed here. `role` comes from the list link and only picks the wording of the cancel text.
 */
export function BookingDetail({ bookingId, role = "client", api = defaultApi }: { bookingId: string; role?: Role; api?: BookingsApi }) {
  const [state, setState] = useState<State>({ kind: "loading" });
  const [open, setOpen] = useState<Open>(null);
  const [done, setDone] = useState<string | null>(null);
  const [round, setRound] = useState(0);
  const refresh = useCallback(() => { setOpen(null); setDone(null); setRound((n) => n + 1); }, []);

  useEffect(() => {
    let live = true;
    void api.get(bookingId).then((r) => {
      if (!live) return;
      if (r.ok) return setState({ kind: "ready", booking: r.value });
      if (r.status === 401) return toSignIn();
      setState({ kind: "error", failure: r });
    });
    return () => { live = false; };
  }, [api, bookingId, round]);

  if (state.kind === "loading") return <p><Spinner /> {mb("detail.loading")}</p>;
  if (state.kind === "error") return <Alert variant="danger">{mb(failureKey(state.failure))}</Alert>;

  const b = state.booking;
  const meeting = safeHttpUrl(b.meetingUrl ?? undefined);
  const common = {
    api, bookingId, onRefresh: refresh, onClose: () => setOpen(null),
    onDone: (next: BookingDetailView, message: string) => { setState({ kind: "ready", booking: next }); setOpen(null); setDone(message); },
  };
  const hasAction = b.canCancel || b.canDispute || b.canReportNoShow;
  return (
    <div className={styles.layout}>
      {done && <Alert variant="success">{done}</Alert>}
      <Card>
        <h2>{b.offeringTitle}</h2>
        <dl className={styles.summary}>
          <dt>{mb(role === "client" ? "party.practitioner" : "party.client")}</dt><dd>{otherParty(b, role)}</dd>
          <dt>{mb("detail.when")}</dt><dd>{whenLabel(b)}<br /><span className={styles.note}>{mb("detail.zone")}</span></dd>
          <dt>{mb("detail.duration")}</dt><dd>{mb("detail.minutes", { n: b.durationMinutes })}</dd>
          <dt>{mb("detail.status")}</dt><dd><Badge>{statusLabel(b.status)}</Badge></dd>
          <dt>{mb("detail.price")}</dt><dd>{formatPln(b.price.amountMinor)}</dd>
          <dt>{mb("detail.payment")}</dt>
          <dd>{paymentLabel(b.payment.status)}{b.payment.refunded.amountMinor > 0 && <> · {mb("detail.refunded", { amount: formatPln(b.payment.refunded.amountMinor) })}</>}</dd>
          {b.fee && <><dt>{mb("detail.fee")}</dt><dd>{formatPln(b.fee.amountMinor)}</dd></>}
          {b.payout && <><dt>{mb("detail.payout")}</dt><dd>{formatPln(b.payout.amountMinor)}</dd></>}
        </dl>
        {meeting && <p><a href={meeting} rel="noopener noreferrer" target="_blank">{mb("detail.meeting")}</a></p>}
      </Card>
      {b.brief && (
        <Card>
          <h2>{mb(role === "client" ? "detail.briefOwn" : "detail.brief")}</h2>
          <p className={styles.brief}>{b.brief.text}</p>
        </Card>
      )}
      {hasAction && !open && (
        <div className={styles.actions}>
          {b.canCancel && <Button variant="danger" onClick={() => setOpen("cancel")}>{mb("actions.cancel")}</Button>}
          {b.canDispute && <Button variant="secondary" onClick={() => setOpen("dispute")}>{mb("actions.dispute")}</Button>}
          {b.canReportNoShow && <Button variant="secondary" onClick={() => setOpen("noShow")}>{mb("actions.noShow")}</Button>}
        </div>
      )}
      {open === "cancel" && b.canCancel && <CancelPanel {...common} role={role} />}
      {open === "dispute" && b.canDispute && <DisputePanel {...common} />}
      {open === "noShow" && b.canReportNoShow && <NoShowPanel {...common} />}
      <div className={styles.actions}>
        {b.canReview && <LinkButton href={reviewHref(b.id)}>{mb("detail.review")}</LinkButton>}
        <Link href={bookingsHref}>{mb("detail.back")}</Link>
      </div>
    </div>
  );
}
