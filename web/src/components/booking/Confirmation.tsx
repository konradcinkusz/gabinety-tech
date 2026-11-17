"use client";
import { Alert, Button, Card, LinkButton, Spinner } from "@/components/ui";
import { formatPln } from "@/components/ui/format";
import { dayHeading, warsawDate, warsawTime } from "@/components/slots/slotTime";
import { createBookingApi, type BookingApi } from "./api";
import styles from "./booking.module.css";
import { safeHttpUrl } from "./lib";
import { bk } from "./strings";
import { useBooking } from "./useBooking";

const defaultApi = createBookingApi();

/** The booking as the server reports it: success is claimed only for `Confirmed`, and the meeting link only when the server sent one. */
export function Confirmation({ bookingId, api = defaultApi }: { bookingId: string; api?: BookingApi }) {
  const { state, reload } = useBooking(api, bookingId);
  if (state.status === "loading") return <p><Spinner /> {bk("checkout.loading")}</p>;
  if (state.status === "error") return <Alert variant="danger">{bk("checkout.loadFailed")}</Alert>;
  const b = state.booking;
  const meeting = b.status === "Confirmed" ? safeHttpUrl(b.meetingUrl) : null;
  return (
    <div className={styles.layout}>
      {b.status === "Confirmed" ? <Alert variant="success">{bk("confirmation.confirmed")}</Alert>
      : b.status === "PendingPayment" ? <Alert variant="info">{bk("confirmation.pending")} <Button variant="secondary" onClick={reload}>{bk("confirmation.refresh")}</Button></Alert>
      : <Alert variant="warning">{bk("confirmation.other")}</Alert>}
      <Card>
        <h2>{b.offeringTitle}</h2>
        <p>{b.practitioner.displayName} · {dayHeading(warsawDate(b.startUtc))}, {warsawTime(b.startUtc)} · {formatPln(b.price.amountMinor)}</p>
        {meeting && <p><a href={meeting} rel="noopener noreferrer" target="_blank">{bk("confirmation.meeting")}</a></p>}
      </Card>
      <LinkButton variant="secondary" href="/programisci">{bk("confirmation.toSearch")}</LinkButton>
    </div>
  );
}
