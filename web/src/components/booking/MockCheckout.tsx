"use client";
import { useEffect, useRef, useState } from "react";
import { Alert, Button, Card, LinkButton, Spinner } from "@/components/ui";
import { formatPln } from "@/components/ui/format";
import { createBookingApi, type BookingApi } from "./api";
import styles from "./booking.module.css";
import { clock } from "./lib";
import { bk, type BookingTextKey } from "./strings";
import { useBooking } from "./useBooking";

const defaultApi = createBookingApi();
const defaultNavigate = (to: string) => window.location.assign(to);

/** The stand-in for the payment provider's page (the route exists only in Mock mode). Counts the 10 minute hold down and simulates the outcome. */
export function MockCheckout({ bookingId, api = defaultApi, navigate = defaultNavigate }: { bookingId: string; api?: BookingApi; navigate?: (to: string) => void }) {
  const { state } = useBooking(api, bookingId);
  const [now, setNow] = useState(() => Date.now());
  const [busy, setBusy] = useState(false);
  const [done, setDone] = useState(false); // the payment failed: nothing more to simulate
  const [problem, setProblem] = useState<BookingTextKey | null>(null);
  const inflight = useRef(false);

  useEffect(() => {
    const id = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(id);
  }, []);

  if (state.status === "loading") return <p><Spinner /> {bk("checkout.loading")}</p>;
  if (state.status === "error") return <Alert variant="danger">{bk("checkout.loadFailed")}</Alert>;
  const b = state.booking;
  const profile = <LinkButton variant="secondary" href={`/gabinet/${b.practitioner.slug}`}>{bk("checkout.toProfile")}</LinkButton>;
  if (b.status !== "PendingPayment") {
    return (
      <Alert variant="info">
        {bk("checkout.closed")}{" "}
        {b.status === "Confirmed" ? <LinkButton href={`/rezerwacja/potwierdzenie/${b.id}`}>{bk("checkout.toBooking")}</LinkButton> : profile}
      </Alert>
    );
  }
  const left = b.holdExpiresAt ? Date.parse(b.holdExpiresAt) - now : Infinity;
  const expired = left <= 0;

  async function simulate(outcome: "succeeded" | "failed") {
    if (inflight.current) return;
    inflight.current = true;
    setBusy(true);
    setProblem(null);
    const r = await api.simulate(bookingId, outcome);
    inflight.current = false;
    if (r.ok && outcome === "succeeded") return navigate(`/rezerwacja/potwierdzenie/${bookingId}`);
    setBusy(false);
    if (r.ok) return setDone(true);
    setProblem(r.status === 403 ? "checkout.notMock" : "checkout.error");
  }

  const locked = busy || expired || done;
  return (
    <div className={styles.layout}>
      <p className={styles.note}>{bk("checkout.intro")}</p>
      <Card>
        <h2>{b.offeringTitle}</h2>
        <p>{b.practitioner.displayName} · {formatPln(b.price.amountMinor)}</p>
        {!expired && b.holdExpiresAt && <p>{bk("checkout.hold", { time: "" })}<span className={styles.countdown}>{clock(left)}</span></p>}
      </Card>
      {expired && <Alert variant="warning">{bk("checkout.expired")} {profile}</Alert>}
      {done && <Alert variant="danger">{bk("checkout.failed")} {profile}</Alert>}
      {problem && <Alert variant="danger">{bk(problem)}</Alert>}
      <div className={styles.actions}>
        <Button onClick={() => void simulate("succeeded")} disabled={locked}>{bk("checkout.succeed")}</Button>
        <Button variant="danger" onClick={() => void simulate("failed")} disabled={locked}>{bk("checkout.fail")}</Button>
      </div>
    </div>
  );
}
