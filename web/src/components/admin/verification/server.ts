// Server side of the verification queue: the role check at the page, and the loader that asks Directory with the user's own token.
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { forgetBackend, resolveBackend } from "@/lib/api/backends";
import { backendFetch } from "@/lib/api/http";
import { ACCESS_COOKIE } from "@/lib/session/cookies";
import { readIdentity } from "@/lib/session/claims";
import { PAGE_LIMIT, QUEUE_STATUSES, type AdminPractitionerDetail, type AdminPractitionerPage, type AdminVerificationEvent, type PractitionerStatus } from "./api";

export type AdminGate = { ok: true; token: string } | { ok: false };

/** The edge guard already redirects a non-admin; this is the second check, at the page. The backend answers 403 as the third. */
export async function requireAdmin(path: string): Promise<AdminGate> {
  const token = (await cookies()).get(ACCESS_COOKIE)?.value;
  const who = readIdentity(token);
  if (!token || !who) redirect("/logowanie?redirect=" + encodeURIComponent(path));
  return who.isAdmin ? { ok: true, token } : { ok: false };
}

export type Queue = { state: "ready"; page: AdminPractitionerPage } | { state: "forbidden" } | { state: "unavailable" };

export function statusOf(raw: string | undefined): PractitionerStatus {
  return (QUEUE_STATUSES as readonly string[]).includes(raw ?? "") ? (raw as PractitionerStatus) : "Submitted";
}

export async function loadQueue(token: string, status: PractitionerStatus, cursor?: string): Promise<Queue> {
  const base = await resolveBackend("directory");
  if (!base) return { state: "unavailable" };
  const q = new URLSearchParams({ status, limit: String(PAGE_LIMIT) });
  if (cursor) q.set("cursor", cursor);
  try {
    const res = await backendFetch(`${base}/api/v1/admin/practitioners?${q}`, {
      headers: { Accept: "application/json", Authorization: `Bearer ${token}` },
      signal: AbortSignal.timeout(5_000),
      cache: "no-store",
    });
    if (res.status === 403) return { state: "forbidden" };
    if (!res.ok) return { state: "unavailable" };
    const body = (await res.json().catch(() => null)) as Partial<AdminPractitionerPage> | null;
    return { state: "ready", page: { items: body?.items ?? [], nextCursor: body?.nextCursor ?? null } };
  } catch {
    forgetBackend("directory");
    return { state: "unavailable" };
  }
}

export type PractitionerDetailLoad =
  | { state: "ready"; detail: AdminPractitionerDetail; events: AdminVerificationEvent[] | null }
  | { state: "forbidden" }
  | { state: "notFound" }
  | { state: "unavailable" };

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
/** Only a well-formed id is ever put into a backend path. */
export const idOf = (raw: unknown): string | null => (typeof raw === "string" && GUID.test(raw) ? raw : null);

/** The profile and its event history, asked in parallel with the user's own token. A failing history does not hide the profile (events: null). */
export async function loadPractitioner(token: string, id: string): Promise<PractitionerDetailLoad> {
  const base = await resolveBackend("directory");
  if (!base) return { state: "unavailable" };
  const get = (suffix: string) =>
    backendFetch(`${base}/api/v1/admin/practitioners/${id}${suffix}`, {
      headers: { Accept: "application/json", Authorization: `Bearer ${token}` },
      signal: AbortSignal.timeout(5_000),
      cache: "no-store",
    });
  try {
    const [d, e] = await Promise.all([get(""), get("/verification-events").catch(() => null)]);
    if (d.status === 403 || e?.status === 403) return { state: "forbidden" };
    if (d.status === 404) return { state: "notFound" };
    if (!d.ok) return { state: "unavailable" };
    const detail = (await d.json().catch(() => null)) as AdminPractitionerDetail | null;
    if (!detail || typeof detail.id !== "string") return { state: "unavailable" };
    const body = e?.ok ? ((await e.json().catch(() => null)) as { items?: AdminVerificationEvent[] } | null) : null;
    return { state: "ready", detail, events: body && Array.isArray(body.items) ? body.items : null };
  } catch {
    forgetBackend("directory");
    return { state: "unavailable" };
  }
}
