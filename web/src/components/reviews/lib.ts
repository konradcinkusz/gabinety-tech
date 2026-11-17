import { authPath } from "@/components/auth/returnTo";
import type { Failure } from "./api";
import { mr, type ReviewsTextKey } from "./strings";

export const RATING_MIN = 1;
export const RATING_MAX = 5;
export const TEXT_MIN = 20;
export const TEXT_MAX = 2000; // CONTRACTS 5.1, D-29
export const REPLY_MIN = 10;
export const REPLY_MAX = 1000; // CONTRACTS 5.2

/**
 * The reply form calls `POST /api/v1/reviews/{id}/reply` (T-05.02b, merged: only the reviewed practitioner, once, to a Published review;
 * the reply passes the same leakage rules as a review). This ONE constant switches the form on; it was off while the endpoint did not exist.
 */
export const REPLY_ENABLED = true;

/** What to tell the user for a failed call, from its code and status only (never from the server's title or detail). */
export function failureKey(f: Failure): ReviewsTextKey {
  if (f.status === 0) return "error.network";
  if (f.code === "dependency_unavailable" || f.status === 503) return "error.unavailable";
  if (f.code === "not_found" || f.status === 404) return "error.notFound";
  if (f.code === "validation_failed" || f.status === 400 || f.status === 422) return "error.validation";
  if (f.code === "forbidden" || f.status === 403) return "error.forbidden";
  if (f.code === "rate_limited" || f.status === 429) return "error.rateLimited";
  return "error.generic";
}

/** Only an explicit Published is announced as published; Held and anything unknown get the held wording (never promise publication). */
export const isPublished = (status: string): boolean => status === "Published";

/** A signed-out answer sends the user to sign in and back. */
const go = (to: string) => window.location.assign(to);
export function toSignIn(): void {
  go(authPath("/logowanie", window.location.pathname + window.location.search));
}

export const ratingLabel = (n: number): string => mr("list.rating", { n });
