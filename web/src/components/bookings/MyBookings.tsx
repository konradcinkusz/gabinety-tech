"use client";
import Link from "next/link";
import { useEffect, useRef, useState } from "react";
import { Alert, Badge, Button, Card, Select, Spinner } from "@/components/ui";
import { formatPln } from "@/components/ui/format";
import { createBookingsApi, type BookingDetailView, type BookingsApi, type Failure } from "./api";
import styles from "./bookings.module.css";
import { failureKey, otherParty, STATUSES, statusLabel, toSignIn, whenLabel } from "./lib";
import { bookingHref, type Role } from "./routes";
import { mb } from "./strings";

const defaultApi = createBookingsApi();
type State = { items: BookingDetailView[]; next?: string; loading: boolean; error?: Failure };

/** The signed-in user's bookings for one role, filtered by status, newest start first, a page at a time (cursor). */
export function MyBookings({ api = defaultApi, initialRole = "client", initialStatus = "" }: { api?: BookingsApi; initialRole?: Role; initialStatus?: string }) {
  const [role, setRole] = useState<Role>(initialRole);
  const [status, setStatus] = useState(initialStatus);
  const [state, setState] = useState<State>({ items: [], loading: true });
  const ticket = useRef(0);

  function request(cursor?: string) {
    const mine = ++ticket.current;
    void api.list(role, status || undefined, cursor).then((r) => {
      if (mine !== ticket.current) return; // a newer request (role or filter changed) owns the list
      if (!r.ok) {
        if (r.status === 401) return toSignIn();
        return setState((s) => ({ items: cursor ? s.items : [], loading: false, error: r }));
      }
      setState((s) => ({ items: cursor ? [...s.items, ...r.value.items] : r.value.items, next: r.value.nextCursor ?? undefined, loading: false }));
    });
  }
  useEffect(() => {
    request();
    // eslint-disable-next-line react-hooks/exhaustive-deps -- reload exactly when the listing changes
  }, [api, role, status]);
  const reset = () => setState({ items: [], loading: true });
  const more = () => {
    setState((s) => ({ ...s, loading: true }));
    request(state.next);
  };

  return (
    <div className={styles.layout}>
      <div className={styles.controls}>
        <div role="group" aria-label={mb("role.label")} className={styles.actions}>
          {(["client", "practitioner"] as const).map((r) => (
            <Button key={r} variant={r === role ? "primary" : "secondary"} aria-pressed={r === role} onClick={() => { if (r !== role) { reset(); setRole(r); } }}>{mb(`role.${r}`)}</Button>
          ))}
        </div>
        <label className={styles.filter}>
          {mb("list.statusLabel")}
          <Select value={status} onChange={(e) => { reset(); setStatus(e.target.value); }}>
            <option value="">{mb("list.allStatuses")}</option>
            {STATUSES.map((s) => <option key={s} value={s}>{statusLabel(s)}</option>)}
          </Select>
        </label>
      </div>
      {state.error && <Alert variant="danger">{mb(failureKey(state.error))}</Alert>}
      {state.items.length === 0 && !state.loading && !state.error && <p>{mb("list.empty")}</p>}
      <ul className={styles.list}>
        {state.items.map((b) => (
          <li key={b.id}>
            <Card>
              <h2 className={styles.rowTitle}>{b.offeringTitle}</h2>
              <p className={styles.note}>{otherParty(b, role)} · {whenLabel(b)} · {formatPln(b.price.amountMinor)}</p>
              <p><Badge>{statusLabel(b.status)}</Badge> <Link href={bookingHref(b.id, role)}>{mb("list.details")}</Link></p>
            </Card>
          </li>
        ))}
      </ul>
      {state.loading && <p><Spinner /> {mb("list.loading")}</p>}
      {state.next && !state.loading && <Button variant="secondary" onClick={more}>{mb("list.more")}</Button>}
    </div>
  );
}
