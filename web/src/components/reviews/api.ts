// Shapes and calls of reviews (CONTRACTS 5.1 and 5.2). The generated Reputation types do not describe these answers, so they are declared by
// hand from src/Gabinety.Contracts/Reputation. Everything goes through the BFF catch-all (CONTRACTS 7). No call here logs a body: the review
// text and the reply are user free text, and nothing from an error answer except its machine `code` is kept.
export type ReviewStatus = "Published" | "Held" | "Rejected";
export type CreateReviewResult = { reviewId: string; status: ReviewStatus | string };
export type ReplyView = { text: string; repliedAt: string };
export type ReviewView = { id: string; rating: number; text: string; createdAt: string; reply?: ReplyView | null };
export type ReviewPage = { items: ReviewView[]; nextCursor?: string | null };
export type MeView = { userId: string; practitionerId?: string | null };

export type Failure = { status: number; code: string; errors: Record<string, string[]> };
export type Outcome<T> = { ok: true; value: T } | ({ ok: false } & Failure);

const REPUTATION = "/api/proxy/reputation/api/v1";
const DIRECTORY = "/api/proxy/directory/api/v1";
type Fetch = typeof fetch;

export interface ReviewsApi {
  create(bookingId: string, rating: number, text: string): Promise<Outcome<CreateReviewResult>>;
  me(): Promise<Outcome<MeView>>;
  list(practitionerId: string, cursor: string | undefined): Promise<Outcome<ReviewPage>>;
  reply(reviewId: string, text: string): Promise<Outcome<unknown>>;
}

export function createReviewsApi(doFetch: Fetch = (...a) => fetch(...a)): ReviewsApi {
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
      return { ok: false, status: res.status, code: typeof p?.code === "string" ? p.code : "unknown", errors: fieldNames(p?.errors) };
    }
    const value = (await res.json().catch(() => ({}))) as T;
    return { ok: true, value };
  }
  const id = encodeURIComponent;
  return {
    create: (bookingId, rating, text) => call("POST", `${REPUTATION}/reviews`, { bookingId, rating, text }),
    me: () => call("GET", `${DIRECTORY}/me`),
    list: (practitionerId, cursor) => {
      const q = new URLSearchParams({ limit: "20" });
      if (cursor) q.set("cursor", cursor);
      return call("GET", `${REPUTATION}/practitioners/${id(practitionerId)}/reviews?${q}`);
    },
    reply: (reviewId, text) => call("POST", `${REPUTATION}/reviews/${id(reviewId)}/reply`, { text }),
  };
}

/** Only the field names of the server's `errors` are kept: the messages are English codes and must not be shown. */
function fieldNames(raw: unknown): Record<string, string[]> {
  const out: Record<string, string[]> = {};
  if (raw && typeof raw === "object") for (const k of Object.keys(raw)) out[k.toLowerCase()] = [];
  return out;
}
