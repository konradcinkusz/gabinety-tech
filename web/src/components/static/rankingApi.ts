// Server-side read of the public ranking rules (CONTRACTS 3.2), the single source of /zasady-rankingu. Same shared read (timeout, cache,
// 429 retry) as lib/seo/publicApi.ts; never used by the browser. A failure carries a status only, never a body.
import { DirectoryUnavailableError, directoryGet } from "@/lib/seo/publicApi";
import type { RankingRules } from "./ranking";

export async function getRankingRules(): Promise<RankingRules> {
  const res = await directoryGet("/api/v1/ranking-rules");
  if (!res.ok) throw new DirectoryUnavailableError(res.status);
  return (await res.json()) as RankingRules;
}
