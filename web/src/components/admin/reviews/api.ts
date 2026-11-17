// Shapes of the Reputation admin moderation API (CONTRACTS 5.2), declared by hand: the generated types of T-07.01 do not know /api/v1/admin.
// The review text and the moderator note are user free text shown to administrators only: they are never logged and never put in a message.
export type ReviewStatus = "Held" | "Published" | "Rejected";
export type TriageFlag = "none" | "hold" | "unavailable";
export type Decision = "publish" | "reject";

export type AdminReview = {
  id: string;
  practitionerId: string;
  rating: number;
  text?: string | null;
  status: ReviewStatus;
  holdReasons?: string[] | null;
  triageFlag?: string | null;
  triageModelId?: string | null;
  createdAt?: string | null;
  publishedAt?: string | null;
  moderatedBy?: string | null;
  moderationNote?: string | null;
};
export type AdminReviewPage = { items: AdminReview[]; nextCursor?: string | null };

export const REVIEW_STATUSES = ["Held", "Published", "Rejected"] as const satisfies readonly ReviewStatus[];
export const REASONS = ["spam", "abuse", "personal_data", "off_topic", "threat"] as const;
export const PAGE_LIMIT = 20;
export const REASON_MAX = 500;

/** The API wants a reason of 1 to 500 characters after trimming. */
export const reasonProblem = (text: string): "invalid" | null => (text.trim().length >= 1 && text.trim().length <= REASON_MAX ? null : "invalid");

export type DecisionResult = { ok: true } | { ok: false; status: number; code: string };
const BASE = "/api/proxy/reputation/api/v1";

export async function runDecision(id: string, action: Decision, reason?: string, doFetch: typeof fetch = (...a) => fetch(...a)): Promise<DecisionResult> {
  let res: Response;
  try {
    res = await doFetch(`${BASE}/admin/reviews/${encodeURIComponent(id)}/${action}`, {
      method: "POST",
      headers: action === "reject" ? { Accept: "application/json", "Content-Type": "application/json" } : { Accept: "application/json" },
      ...(action === "reject" ? { body: JSON.stringify({ reason: (reason ?? "").trim() }) } : {}),
      cache: "no-store",
    });
  } catch {
    return { ok: false, status: 0, code: "network" };
  }
  if (res.ok) return { ok: true };
  const j = (await res.json().catch(() => null)) as { code?: unknown } | null;
  return { ok: false, status: res.status, code: typeof j?.code === "string" ? j.code : "unknown" };
}
