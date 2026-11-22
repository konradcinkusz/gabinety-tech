import { identity } from "./identity";
import type { TokenSet } from "./cookies";

// One in-flight refresh per refresh token: a burst of parallel requests must not replay the
// single-use token. The entry is dropped as soon as the promise settles.
const inflight = new Map<string, Promise<TokenSet | null>>();

export function refreshOnce(refreshToken: string, clientIp?: string): Promise<TokenSet | null> {
  const existing = inflight.get(refreshToken);
  if (existing) return existing;
  const p = identity
    .refresh(refreshToken, clientIp)
    .then((r) => (r.kind === "tokens" ? r.tokens : null))
    .catch(() => null)
    .finally(() => inflight.delete(refreshToken));
  inflight.set(refreshToken, p);
  return p;
}
