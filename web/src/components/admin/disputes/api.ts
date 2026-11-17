// Shapes of the Booking admin disputes API (CONTRACTS 4.3), declared by hand: the generated types of T-07.01 do not know /api/v1/admin.
// The dispute text and the resolution note are free text shown to administrators only: they are never logged and never put in a message.
export type DisputeKind = "not_as_described" | "practitioner_no_show" | "other";
export type Resolution = "release" | "refund_full" | "refund_partial";
export type DisputeStatus = "open" | "resolved";

export type AdminDispute = {
  bookingId: string;
  clientUserId: string;
  practitionerUserId: string;
  kind?: string | null;
  text?: string | null;
  openedAt?: string | null;
  resolvedAt?: string | null;
  resolution?: string | null;
  note?: string | null;
};
export type AdminDisputePage = { items: AdminDispute[]; nextCursor?: string | null };

export const DISPUTE_STATUSES = ["open", "resolved"] as const satisfies readonly DisputeStatus[];
export const RESOLUTIONS = ["release", "refund_full", "refund_partial"] as const satisfies readonly Resolution[];
export const PAGE_LIMIT = 20;
export const NOTE_MIN = 10;
export const NOTE_MAX = 500;

export const noteProblem = (text: string): "invalid" | null => (text.trim().length >= NOTE_MIN && text.trim().length <= NOTE_MAX ? null : "invalid");

export type Parsed = { ok: true; grosz: number } | { ok: false; problem: "format" | "decimals" };
/** "12,50" and "12.50" -> 1250. Integer arithmetic on the digits: no floating point, more than two decimals is refused (never rounded). */
export function parsePlnToGrosz(raw: string): Parsed {
  const m = /^(\d{1,9})(?:[.,](\d*))?$/.exec(raw.trim());
  if (!m) return { ok: false, problem: "format" };
  const frac = m[2] ?? "";
  if (frac.length > 2) return { ok: false, problem: "decimals" };
  if (m[0].trim().endsWith(",") || m[0].trim().endsWith(".")) return { ok: false, problem: "format" };
  return { ok: true, grosz: Number(m[1]) * 100 + Number(frac.padEnd(2, "0")) };
}

/** A partial refund is strictly between 0 and the price (1 to price - 1). Without a known price the server checks the upper bound. */
export function amountProblem(grosz: number, price: number | null): "range" | null {
  if (grosz < 1) return "range";
  return price !== null && grosz > price - 1 ? "range" : null;
}

export type ResolveBody = { resolution: Resolution; refundMinor?: number; note: string };
export type ResolveResult = { ok: true } | { ok: false; status: number; code: string; fields: string[] };
const BASE = "/api/proxy/booking/api/v1";

export async function runResolve(bookingId: string, body: ResolveBody, doFetch: typeof fetch = (...a) => fetch(...a)): Promise<ResolveResult> {
  let res: Response;
  try {
    res = await doFetch(`${BASE}/admin/bookings/${encodeURIComponent(bookingId)}/resolve`, {
      method: "POST",
      headers: { Accept: "application/json", "Content-Type": "application/json" },
      body: JSON.stringify({ ...body, note: body.note.trim() }),
      cache: "no-store",
    });
  } catch {
    return { ok: false, status: 0, code: "network", fields: [] };
  }
  if (res.ok) return { ok: true };
  const j = (await res.json().catch(() => null)) as { code?: unknown; errors?: unknown } | null;
  const fields = j && typeof j.errors === "object" && j.errors ? Object.keys(j.errors) : [];
  return { ok: false, status: res.status, code: typeof j?.code === "string" ? j.code : "unknown", fields };
}

/** The list item carries no price: an administrator may read the booking (CONTRACTS 4.2), so the panel asks for it. Null when it cannot be read. */
export async function fetchPrice(bookingId: string, doFetch: typeof fetch = (...a) => fetch(...a)): Promise<number | null> {
  try {
    const res = await doFetch(`${BASE}/bookings/${encodeURIComponent(bookingId)}`, { headers: { Accept: "application/json" }, cache: "no-store" });
    if (!res.ok) return null;
    const j = (await res.json()) as { price?: { amountMinor?: unknown } };
    const v = j.price?.amountMinor;
    return typeof v === "number" && Number.isSafeInteger(v) && v > 0 ? v : null;
  } catch {
    return null;
  }
}
