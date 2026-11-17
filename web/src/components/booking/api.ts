// Shapes and calls of the booking flow (CONTRACTS 4.2, 4.4, 6). The generated types do not know them yet, so they are declared by hand.
// The browser talks to its own origin only: every call goes through the BFF catch-all (CONTRACTS 7). No call here logs a body.
export type Money = { amountMinor: number; currency: "PLN" };
export type BriefInput = { text: string; problemSlug?: string; aiAssisted: boolean };
export type CreateBookingInput = { offeringId: string; startUtc: string; brief: BriefInput };
export type BookingCreated = { bookingId: string; status: "PendingPayment"; holdExpiresAt: string; checkoutUrl: string };
export type BriefSuggestion = {
  problemSlug: string;
  techSlugs: string[];
  summary: string;
  clarifyingQuestions: string[];
  suggestedDurationMinutes: 30 | 60;
  confidence: "low" | "medium" | "high";
  modelId: string;
  promptVersion: string;
};
export type BookingView = {
  id: string;
  status: string;
  startUtc: string;
  endUtc: string;
  durationMinutes: number;
  offeringTitle: string;
  price: Money;
  practitioner: { id: string; displayName: string; slug: string };
  meetingUrl?: string;
  holdExpiresAt?: string;
};

/** `status` 0 is a network failure. `code` is the machine code of the problem answer, never its title or detail. */
export type Outcome<T> = { ok: true; value: T } | { ok: false; status: number; code: string };

const BOOKING = "/api/proxy/booking/api/v1";
const ASSISTANT = "/api/proxy/assistant/api/v1";
type Fetch = typeof fetch;

export interface BookingApi {
  create(idempotencyKey: string, input: CreateBookingInput): Promise<Outcome<BookingCreated>>;
  get(bookingId: string): Promise<Outcome<BookingView>>;
  simulate(bookingId: string, outcome: "succeeded" | "failed"): Promise<Outcome<void>>;
  suggest(text: string): Promise<Outcome<BriefSuggestion>>;
}

export function createBookingApi(doFetch: Fetch = (...a) => fetch(...a)): BookingApi {
  async function call<T>(method: string, url: string, body?: unknown, headers: Record<string, string> = {}): Promise<Outcome<T>> {
    let res: Response;
    try {
      res = await doFetch(url, {
        method,
        headers: { Accept: "application/json", ...(body === undefined ? {} : { "Content-Type": "application/json" }), ...headers },
        body: body === undefined ? undefined : JSON.stringify(body),
        cache: "no-store",
      });
    } catch {
      return { ok: false, status: 0, code: "network" };
    }
    if (!res.ok) {
      const p = (await res.json().catch(() => null)) as { code?: unknown } | null;
      return { ok: false, status: res.status, code: typeof p?.code === "string" ? p.code : "unknown" };
    }
    return { ok: true, value: (await res.json().catch(() => undefined)) as T };
  }
  return {
    create: (key, input) => call("POST", `${BOOKING}/bookings`, input, { "Idempotency-Key": key }),
    get: (id) => call("GET", `${BOOKING}/bookings/${encodeURIComponent(id)}`),
    simulate: (bookingId, outcome) => call("POST", `${BOOKING}/payments/simulate`, { bookingId, outcome }),
    suggest: (text) => call("POST", `${ASSISTANT}/assistant/brief`, { text }),
  };
}
