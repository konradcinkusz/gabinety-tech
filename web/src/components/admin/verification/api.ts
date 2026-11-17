// Shapes of the Directory admin API (CONTRACTS 3.4), declared by hand: the generated types of T-07.01 do not know /api/v1/admin.
// The browser talks to its own origin only: every call goes through the BFF catch-all (CONTRACTS 7). Reasons are free text:
// they are sent in the body and never logged, never put in a message.
export type PractitionerStatus = "Draft" | "Submitted" | "Approved" | "Rejected" | "Suspended";
export type AdminPractitionerRow = {
  id: string;
  slug: string;
  displayName: string;
  headline: string;
  status: PractitionerStatus;
  statusReason?: string | null;
  githubLinked: boolean;
  githubLogin?: string | null;
  nipValid: boolean;
  createdAt: string;
  submittedAt?: string | null;
};
export type AdminPractitionerPage = { items: AdminPractitionerRow[]; nextCursor?: string | null };
export type AdminAction = "approve" | "reject" | "suspend" | "reinstate";

/** The practitioner state machine (SPEC 8.2): what an administrator may do from each status. The API still decides. */
const ALLOWED: Record<PractitionerStatus, AdminAction[]> = {
  Draft: [],
  Submitted: ["approve", "reject"],
  Approved: ["suspend"],
  Suspended: ["reinstate"],
  Rejected: [],
};
export const allowedActions = (status: PractitionerStatus): AdminAction[] => ALLOWED[status];
export const needsReason = (a: AdminAction) => a === "reject" || a === "suspend";

export const REASON_MIN = 10;
export const REASON_MAX = 500;
/** Null when the (trimmed) reason is acceptable. */
export function reasonProblem(text: string): "invalid" | null {
  const n = text.trim().length;
  return n >= REASON_MIN && n <= REASON_MAX ? null : "invalid";
}

export const QUEUE_STATUSES = ["Submitted", "Approved", "Suspended", "Rejected"] as const satisfies readonly PractitionerStatus[];
export const PAGE_LIMIT = 12;

export type ActionResult = { ok: true } | { ok: false; status: number; code: string };
const BASE = "/api/proxy/directory/api/v1/admin/practitioners";

export async function runAction(id: string, action: AdminAction, reason?: string, doFetch: typeof fetch = (...a) => fetch(...a)): Promise<ActionResult> {
  let res: Response;
  try {
    res = await doFetch(`${BASE}/${encodeURIComponent(id)}/${action}`, {
      method: "POST",
      headers: reason === undefined ? { Accept: "application/json" } : { Accept: "application/json", "Content-Type": "application/json" },
      body: reason === undefined ? undefined : JSON.stringify({ reason: reason.trim() }),
      cache: "no-store",
    });
  } catch {
    return { ok: false, status: 0, code: "network" };
  }
  if (res.ok) return { ok: true };
  const body = (await res.json().catch(() => null)) as { code?: unknown } | null;
  return { ok: false, status: res.status, code: typeof body?.code === "string" ? body.code : "unknown" };
}
