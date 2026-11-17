// Server side of the ledger page: the CSV is streamed from Booking to the browser with the administrator's own token; it is never buffered.
import { forgetBackend, resolveBackend } from "@/lib/api/backends";
import { backendFetch } from "@/lib/api/http";
import { problem } from "@/lib/api/problem";
import { csvFileName } from "./api";

/** Reading the headers is bounded (SPEC 5.1: 5 s); the body is not, because a year of rows streams for longer. A client that leaves cancels it. */
const HEADERS_TIMEOUT_MS = 5_000;

/** `from` and `to` are already validated days. The body is passed on as the same stream; the filename is built here, not copied from the backend. */
export async function streamLedger(token: string, from: string, to: string, signal?: AbortSignal): Promise<Response> {
  const base = await resolveBackend("booking");
  if (!base) return problem(503, "dependency_unavailable");
  const abort = new AbortController();
  const onClientGone = () => abort.abort();
  signal?.addEventListener("abort", onClientGone, { once: true });
  const timer = setTimeout(() => abort.abort(), HEADERS_TIMEOUT_MS);
  try {
    const q = new URLSearchParams({ from, to });
    const res = await backendFetch(`${base}/api/v1/admin/ledger.csv?${q}`, {
      headers: { Accept: "text/csv", Authorization: `Bearer ${token}` },
      signal: abort.signal,
      cache: "no-store",
    });
    clearTimeout(timer);
    if (!res.ok || !res.body) {
      console.warn(`ledger.csv upstream status ${res.status}`); // the status only: no body, address or period
      return refusalOf(res);
    }
    return new Response(res.body, {
      status: 200,
      headers: {
        "Content-Type": "text/csv; charset=utf-8",
        "Content-Disposition": `attachment; filename="${csvFileName(from, to)}"`,
        "Cache-Control": "no-store",
        "X-Content-Type-Options": "nosniff",
      },
    });
  } catch {
    clearTimeout(timer);
    console.warn("ledger.csv upstream unreachable");
    forgetBackend("booking");
    return problem(503, "dependency_unavailable"); // never echo the error: it can carry the backend address
  }
}

const PROBLEM_CODE = /^[a-z][a-z0-9_]{0,63}$/;
const RETRY_AFTER = /^[0-9A-Za-z ,:+-]{1,64}$/;

/** Booking's answer to a refused export, mapped without echoing it: 401/403/404 as they are, 400 with a well-formed problem code, 429 with Retry-After, the rest is an outage. */
async function refusalOf(res: Response): Promise<Response> {
  switch (res.status) {
    case 400: {
      let code = "validation_failed";
      try {
        const found = ((await res.json()) as { code?: unknown } | null)?.code;
        if (typeof found === "string" && PROBLEM_CODE.test(found)) code = found;
      } catch {
        await res.body?.cancel().catch(() => {});
      }
      return problem(400, code);
    }
    case 401:
      await res.body?.cancel();
      return problem(401, "unauthorized");
    case 403:
      await res.body?.cancel();
      return problem(403, "forbidden");
    case 404:
      await res.body?.cancel();
      return problem(404, "not_found");
    case 429: {
      await res.body?.cancel();
      const wait = res.headers.get("retry-after");
      return problem(429, "rate_limited", wait && RETRY_AFTER.test(wait) ? { "Retry-After": wait } : undefined);
    }
    default:
      await res.body?.cancel();
      return problem(503, "dependency_unavailable");
  }
}
