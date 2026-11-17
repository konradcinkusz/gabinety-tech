import { dayHeading, warsawDate, warsawTime } from "@/components/slots/slotTime";
import { authPath } from "@/components/auth/returnTo";
import type { BookingDetailView, Failure } from "./api";
import type { Role } from "./routes";
import { mb, type BookingsTextKey } from "./strings";

export const STATUSES = ["PendingPayment", "Confirmed", "CancelledByClient", "LateCancelledByClient", "CancelledByPractitioner", "Expired", "Completed", "ClientNoShow", "Disputed", "Resolved"] as const;
const PAYMENTS = ["Created", "Succeeded", "Failed", "Refunded", "PartiallyRefunded"];
export const DISPUTE_MIN = 10;
export const DISPUTE_MAX = 1000;
export const REASON_MAX = 500;

export const statusLabel = (s: string): string => ((STATUSES as readonly string[]).includes(s) ? mb(`status.${s}` as BookingsTextKey) : mb("status.unknown"));
export const paymentLabel = (s: string): string => (PAYMENTS.includes(s) ? mb(`payment.${s}` as BookingsTextKey) : mb("payment.unknown"));

/** `niedziela, 28 marca, 13:00-14:00`, in Europe/Warsaw whatever the browser's zone. */
export const whenLabel = (b: Pick<BookingDetailView, "startUtc" | "endUtc">): string =>
  `${dayHeading(warsawDate(b.startUtc))}, ${warsawTime(b.startUtc)}–${warsawTime(b.endUtc)}`;

/** The other party's name when the API sent one, else a neutral label (names come empty until a lookup is built). */
export function otherParty(b: BookingDetailView, role: Role): string {
  const name = (role === "client" ? b.practitioner.displayName : b.client?.displayName)?.trim();
  return name || mb(role === "client" ? "party.practitioner" : "party.client");
}

/** What to tell the user for a failed call, from its code (never from the server's title or detail). */
export function failureKey(f: Failure): BookingsTextKey {
  if (f.status === 0) return "error.network";
  if (f.code === "invalid_transition") return "error.invalidTransition";
  if (f.code === "dependency_unavailable" || f.status === 503) return "error.unavailable";
  if (f.code === "not_found" || f.status === 404) return "error.notFound";
  if (f.code === "validation_failed" || f.code === "invalid_input" || f.status === 400 || f.status === 422) return "error.validation";
  if (f.code === "forbidden" || f.status === 403) return "error.forbidden";
  if (f.code === "rate_limited" || f.status === 429) return "error.rateLimited";
  return "error.generic";
}

/** Per-field Polish messages for the fields the server named in `errors`. */
export function fieldMessages(f: Failure): Record<string, string> {
  const out: Record<string, string> = {};
  for (const k of Object.keys(f.errors)) {
    out[k] = k === "text" ? mb("dispute.tooShort") : k === "reason" ? mb("error.reason") : k === "kind" ? mb("error.kind") : mb("error.field");
  }
  return out;
}

const go = (to: string) => window.location.assign(to);

/** A signed-out answer sends the user to sign in and back. */
export function toSignIn(): void {
  go(authPath("/logowanie", window.location.pathname + window.location.search));
}
