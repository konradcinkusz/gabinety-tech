"use client";
import Link from "next/link";
import { useCallback, useEffect, useState } from "react";
import { Alert, Button, Spinner } from "@/components/ui";
import styles from "./slots.module.css";
import { dayHeading, groupByWarsawDay, slotWindow, warsawTime, withinHorizon, type Slot } from "./slotTime";
import { ss } from "./strings";

type State = { status: "loading" | "ready" | "unavailable"; slots: Slot[]; next: number };

/** Loads slots for one offering through the BFF (never Booking directly) and lists them by Warsaw day, each as a link into the booking flow. The booking page resolves the offering through the public profile, so the link carries `gabinet` (the profile slug) next to `start`. */
export function SlotPicker({ practitionerId, offeringId, profileSlug, now }: { practitionerId: string; offeringId: string; profileSlug: string; now?: Date }) {
  const [clock] = useState(() => now ?? new Date());
  const [state, setState] = useState<State>({ status: "loading", slots: [], next: 0 });

  const fetchWindow = useCallback(
    async (index: number): Promise<Slot[]> => {
      const w = slotWindow(clock, index);
      if (!w) return [];
      const q = new URLSearchParams({ offeringId, from: w.from, to: w.to });
      const res = await fetch(`/api/proxy/booking/api/v1/practitioners/${encodeURIComponent(practitionerId)}/slots?${q}`, { headers: { accept: "application/json" } });
      if (!res.ok) throw new Error(`slots:${res.status}`);
      const body = (await res.json()) as { slots?: Slot[] };
      if (!Array.isArray(body.slots)) throw new Error("slots:shape");
      return withinHorizon(body.slots, clock);
    },
    [offeringId, practitionerId, clock],
  );

  // Initial load: state starts as "loading", so nothing is set before the response arrives.
  useEffect(() => {
    let live = true;
    fetchWindow(0).then(
      (slots) => live && setState({ status: "ready", slots, next: 1 }),
      () => live && setState({ status: "unavailable", slots: [], next: 0 }),
    );
    return () => {
      live = false;
    };
  }, [fetchWindow]);

  const loadMore = async () => {
    setState((s) => ({ ...s, status: "loading" }));
    try {
      const slots = await fetchWindow(state.next);
      setState((s) => ({ status: "ready", slots: [...s.slots, ...slots], next: state.next + 1 }));
    } catch {
      setState((s) => ({ ...s, status: "unavailable" }));
    }
  };

  const hasMore = slotWindow(clock, state.next) !== null;
  if (state.status === "unavailable") {
    return (
      <div className={styles.picker}>
        <Alert variant="warning">{ss("slots.unavailable")}</Alert>
        <Button variant="secondary" onClick={() => void loadMore()}>{ss("slots.retry")}</Button>
      </div>
    );
  }
  const days = groupByWarsawDay(state.slots);
  return (
    <div className={styles.picker} aria-busy={state.status === "loading"} aria-live="polite">
      {days.map((d) => (
        <section key={d.date} aria-label={dayHeading(d.date)}>
          <h4 className={styles.day}>{dayHeading(d.date)}</h4>
          <ul className={styles.times}>
            {d.slots.map((s) => (
              <li key={s.startUtc}>
                <Link className={styles.time} href={`/rezerwacja/${offeringId}?start=${encodeURIComponent(s.startUtc)}&gabinet=${encodeURIComponent(profileSlug)}`}>{warsawTime(s.startUtc)}</Link>
              </li>
            ))}
          </ul>
        </section>
      ))}
      {state.status === "loading" && <p className={styles.note}><Spinner /> {ss("slots.loading")}</p>}
      {state.status === "ready" && days.length === 0 && <p className={styles.note}>{ss("slots.empty")}</p>}
      {state.status === "ready" && hasMore && <Button variant="secondary" onClick={() => void loadMore()}>{ss("slots.more")}</Button>}
      {days.length > 0 && <p className={styles.note}>{ss("slots.timeZone")}</p>}
    </div>
  );
}
