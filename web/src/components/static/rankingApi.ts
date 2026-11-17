// Server-side read of the public ranking rules (CONTRACTS 3.2), the single source of /zasady-rankingu. Same ladder, timeout and cache as
// lib/seo/publicApi.ts; never used by the browser. A failure carries a status only, never a body.
import { resolveBackend } from "@/lib/api/backends";
import { backendFetch } from "@/lib/api/http";
import { DirectoryUnavailableError, REVALIDATE_SECONDS } from "@/lib/seo/publicApi";
import type { RankingRules } from "./ranking";

export async function getRankingRules(): Promise<RankingRules> {
  const base = await resolveBackend("directory");
  if (!base) throw new DirectoryUnavailableError(0);
  let res: Response;
  try {
    res = await backendFetch(`${base}/api/v1/ranking-rules`, {
      headers: { accept: "application/json" },
      signal: AbortSignal.timeout(5_000),
      next: { revalidate: REVALIDATE_SECONDS },
    } as RequestInit);
  } catch {
    throw new DirectoryUnavailableError(0);
  }
  if (!res.ok) throw new DirectoryUnavailableError(res.status);
  return (await res.json()) as RankingRules;
}
