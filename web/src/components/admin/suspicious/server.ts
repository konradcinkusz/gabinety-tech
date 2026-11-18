// Server side of the suspicious-pairs page: the loader that asks Reputation with the user's own token. The role check is the verification page's requireAdmin.
import { forgetBackend, resolveBackend } from "@/lib/api/backends";
import { backendFetch } from "@/lib/api/http";
import { PAGE_LIMIT, type SuspiciousPairPage } from "./api";

export type Pairs = { state: "ready"; page: SuspiciousPairPage } | { state: "forbidden" } | { state: "unavailable" };

/** Booking's cursor, passed through by Reputation: base64url parts joined by a dot. Anything else is dropped rather than forwarded. */
export const cursorOf = (raw: unknown): string | undefined => (typeof raw === "string" && /^[A-Za-z0-9_.-]{1,512}$/.test(raw) ? raw : undefined);

export async function loadPairs(token: string, cursor?: string): Promise<Pairs> {
  const base = await resolveBackend("reputation");
  if (!base) return { state: "unavailable" };
  const q = new URLSearchParams({ limit: String(PAGE_LIMIT) });
  if (cursor) q.set("cursor", cursor);
  try {
    const res = await backendFetch(`${base}/api/v1/admin/suspicious-pairs?${q}`, {
      headers: { Accept: "application/json", Authorization: `Bearer ${token}` },
      signal: AbortSignal.timeout(5_000),
      cache: "no-store",
    });
    if (res.status === 403) return { state: "forbidden" };
    if (!res.ok) return { state: "unavailable" };
    const body = (await res.json().catch(() => null)) as Partial<SuspiciousPairPage> | null;
    return { state: "ready", page: { items: body?.items ?? [], nextCursor: body?.nextCursor ?? null } };
  } catch {
    forgetBackend("reputation");
    return { state: "unavailable" };
  }
}
