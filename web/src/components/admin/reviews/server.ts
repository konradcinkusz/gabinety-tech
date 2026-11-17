// Server side of the moderation page: the loader that asks Reputation with the user's own token. The role check is the verification page's requireAdmin.
import { forgetBackend, resolveBackend } from "@/lib/api/backends";
import { backendFetch } from "@/lib/api/http";
import { PAGE_LIMIT, REVIEW_STATUSES, type AdminReviewPage, type ReviewStatus } from "./api";

export type Reviews = { state: "ready"; page: AdminReviewPage } | { state: "forbidden" } | { state: "unavailable" };

export function statusOf(raw: string | undefined): ReviewStatus {
  return (REVIEW_STATUSES as readonly string[]).includes(raw ?? "") ? (raw as ReviewStatus) : "Held";
}

/** Cursors are opaque base64url; anything else is dropped rather than forwarded. */
export const cursorOf = (raw: unknown): string | undefined => (typeof raw === "string" && /^[A-Za-z0-9_-]{1,200}$/.test(raw) ? raw : undefined);

export async function loadReviews(token: string, status: ReviewStatus, cursor?: string): Promise<Reviews> {
  const base = await resolveBackend("reputation");
  if (!base) return { state: "unavailable" };
  const q = new URLSearchParams({ status, limit: String(PAGE_LIMIT) });
  if (cursor) q.set("cursor", cursor);
  try {
    const res = await backendFetch(`${base}/api/v1/admin/reviews?${q}`, {
      headers: { Accept: "application/json", Authorization: `Bearer ${token}` },
      signal: AbortSignal.timeout(5_000),
      cache: "no-store",
    });
    if (res.status === 403) return { state: "forbidden" };
    if (!res.ok) return { state: "unavailable" };
    const body = (await res.json().catch(() => null)) as Partial<AdminReviewPage> | null;
    return { state: "ready", page: { items: body?.items ?? [], nextCursor: body?.nextCursor ?? null } };
  } catch {
    forgetBackend("reputation");
    return { state: "unavailable" };
  }
}
