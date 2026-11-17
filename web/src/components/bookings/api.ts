// Shapes and calls of "my bookings" (CONTRACTS 4.2, 4.4). The generated types do not know the Booking service yet, so they are declared
// by hand from src/Gabinety.Contracts/Booking. Everything goes through the BFF catch-all (CONTRACTS 7). No call here logs a body:
// the brief, the dispute text and the cancel reason are user free text.
import type { Money } from "@/components/booking/api";
import type { Role } from "./routes";

export type PaymentState = { status: string; refunded: Money };
export type BookingDetailView = {
  id: string;
  status: string;
  statusReason?: string | null;
  startUtc: string;
  endUtc: string;
  durationMinutes: number;
  offeringTitle: string;
  price: Money;
  /** `displayName` and `slug` come empty until a lookup exists. */
  practitioner: { id: string; displayName?: string | null; slug?: string | null };
  client?: { displayName?: string | null } | null;
  brief?: { text: string; problemSlug?: string | null; aiAssisted: boolean } | null;
  meetingUrl?: string | null;
  holdExpiresAt?: string | null;
  payment: PaymentState;
  fee?: Money | null;
  payout?: Money | null;
  canCancel: boolean;
  canDispute: boolean;
  canReportNoShow: boolean;
  canReview: boolean;
};
export type BookingPage = { items: BookingDetailView[]; nextCursor?: string | null };
export type DisputeKind = "not_as_described" | "practitioner_no_show" | "other";

/** `status` 0 is a network failure. `code` is the machine code of the problem answer, never its title or detail. */
export type Failure = { status: number; code: string; errors: Record<string, string[]> };
export type Outcome<T> = { ok: true; value: T } | ({ ok: false } & Failure);

const BASE = "/api/proxy/booking/api/v1";
type Fetch = typeof fetch;

export interface BookingsApi {
  list(role: Role, status: string | undefined, cursor: string | undefined): Promise<Outcome<BookingPage>>;
  get(id: string): Promise<Outcome<BookingDetailView>>;
  cancel(id: string, reason: string | undefined): Promise<Outcome<BookingDetailView>>;
  dispute(id: string, kind: DisputeKind, text: string): Promise<Outcome<BookingDetailView>>;
  reportNoShow(id: string): Promise<Outcome<BookingDetailView>>;
}

export function createBookingsApi(doFetch: Fetch = (...a) => fetch(...a)): BookingsApi {
  async function call<T>(method: string, url: string, body?: unknown): Promise<Outcome<T>> {
    let res: Response;
    try {
      res = await doFetch(url, {
        method,
        headers: { Accept: "application/json", ...(body === undefined ? {} : { "Content-Type": "application/json" }) },
        body: body === undefined ? undefined : JSON.stringify(body),
        cache: "no-store",
      });
    } catch {
      return { ok: false, status: 0, code: "network", errors: {} };
    }
    if (!res.ok) {
      const p = (await res.json().catch(() => null)) as { code?: unknown; errors?: unknown } | null;
      return { ok: false, status: res.status, code: typeof p?.code === "string" ? p.code : "unknown", errors: fieldErrors(p?.errors) };
    }
    const value = (await res.json().catch(() => undefined)) as T | undefined;
    return value === undefined ? { ok: false, status: res.status, code: "unknown", errors: {} } : { ok: true, value };
  }
  const id = encodeURIComponent;
  return {
    list: (role, status, cursor) => {
      const q = new URLSearchParams({ role });
      if (status) q.set("status", status);
      if (cursor) q.set("cursor", cursor);
      return call("GET", `${BASE}/me/bookings?${q}`);
    },
    get: (bookingId) => call("GET", `${BASE}/bookings/${id(bookingId)}`),
    cancel: (bookingId, reason) => call("POST", `${BASE}/bookings/${id(bookingId)}/cancel`, reason ? { reason } : {}),
    dispute: (bookingId, kind, text) => call("POST", `${BASE}/bookings/${id(bookingId)}/dispute`, { kind, text }),
    reportNoShow: (bookingId) => call("POST", `${BASE}/bookings/${id(bookingId)}/report-client-no-show`),
  };
}

/** Only the field names of the server's `errors` are kept: the messages are English and may echo input. */
function fieldErrors(raw: unknown): Record<string, string[]> {
  const out: Record<string, string[]> = {};
  if (raw && typeof raw === "object") for (const k of Object.keys(raw)) out[k.toLowerCase()] = [];
  return out;
}
