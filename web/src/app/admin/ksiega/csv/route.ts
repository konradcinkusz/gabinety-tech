import { problemOf } from "@/components/admin/ledger/api";
import { streamLedger } from "@/components/admin/ledger/server";
import { problem } from "@/lib/api/problem";
import { ACCESS_COOKIE, parseCookies } from "@/lib/session/cookies";
import { readIdentity } from "@/lib/session/claims";

export const dynamic = "force-dynamic";

/** The CSV download of /admin/ksiega. Own route, not the generic proxy: the proxy cuts reads at 5 s and drops Content-Disposition. */
export async function GET(request: Request): Promise<Response> {
  const token = parseCookies(request.headers.get("cookie"))[ACCESS_COOKIE];
  const who = readIdentity(token);
  if (!token || !who) return problem(401, "unauthorized");
  if (!who.isAdmin) return problem(403, "forbidden");
  const q = new URL(request.url).searchParams;
  const from = q.get("from") ?? "";
  const to = q.get("to") ?? "";
  if (problemOf(from, to)) return problem(400, "validation_failed");
  return streamLedger(token, from, to, request.signal);
}
