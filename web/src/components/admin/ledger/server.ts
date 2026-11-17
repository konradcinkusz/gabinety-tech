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
    if (res.status === 403 || res.status === 400) {
      await res.body?.cancel();
      return problem(res.status, res.status === 403 ? "forbidden" : "validation_failed");
    }
    if (!res.ok || !res.body) {
      await res.body?.cancel();
      return problem(503, "dependency_unavailable");
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
    forgetBackend("booking");
    return problem(503, "dependency_unavailable"); // never echo the error: it can carry the backend address
  }
}
