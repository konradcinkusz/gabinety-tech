// Server side of the disputes page: the loader that asks Booking with the user's own token. The role check is the verification page's requireAdmin.
import { forgetBackend, resolveBackend } from "@/lib/api/backends";
import { backendFetch } from "@/lib/api/http";
import { DISPUTE_STATUSES, PAGE_LIMIT, type AdminDisputePage, type DisputeStatus } from "./api";

export type Disputes = { state: "ready"; page: AdminDisputePage } | { state: "forbidden" } | { state: "unavailable" };

export function statusOf(raw: string | undefined): DisputeStatus {
  return (DISPUTE_STATUSES as readonly string[]).includes(raw ?? "") ? (raw as DisputeStatus) : "open";
}

/** Cursors are opaque base64url; anything else is dropped rather than forwarded. */
export const cursorOf = (raw: unknown): string | undefined => (typeof raw === "string" && /^[A-Za-z0-9_-]{1,200}$/.test(raw) ? raw : undefined);

export async function loadDisputes(token: string, status: DisputeStatus, cursor?: string): Promise<Disputes> {
  const base = await resolveBackend("booking");
  if (!base) return { state: "unavailable" };
  const q = new URLSearchParams({ status, limit: String(PAGE_LIMIT) });
  if (cursor) q.set("cursor", cursor);
  try {
    const res = await backendFetch(`${base}/api/v1/admin/disputes?${q}`, {
      headers: { Accept: "application/json", Authorization: `Bearer ${token}` },
      signal: AbortSignal.timeout(5_000),
      cache: "no-store",
    });
    if (res.status === 403) return { state: "forbidden" };
    if (!res.ok) return { state: "unavailable" };
    const body = (await res.json().catch(() => null)) as Partial<AdminDisputePage> | null;
    return { state: "ready", page: { items: body?.items ?? [], nextCursor: body?.nextCursor ?? null } };
  } catch {
    forgetBackend("booking");
    return { state: "unavailable" };
  }
}
