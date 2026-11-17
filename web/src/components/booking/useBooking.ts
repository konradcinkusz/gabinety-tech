"use client";
import { useCallback, useEffect, useState } from "react";
import type { BookingApi, BookingView } from "./api";

export type BookingState = { status: "loading" } | { status: "error" } | { status: "ready"; booking: BookingView };

/** Loads one booking through the BFF; `reload` asks again (for instance after a payment event). */
export function useBooking(api: BookingApi, bookingId: string): { state: BookingState; reload: () => void } {
  const [state, setState] = useState<BookingState>({ status: "loading" });
  const [round, setRound] = useState(0);
  useEffect(() => {
    let live = true;
    void api.get(bookingId).then((r) => {
      if (live) setState(r.ok && r.value ? { status: "ready", booking: r.value } : { status: "error" });
    });
    return () => {
      live = false;
    };
  }, [api, bookingId, round]);
  const reload = useCallback(() => setRound((n) => n + 1), []);
  return { state, reload };
}
