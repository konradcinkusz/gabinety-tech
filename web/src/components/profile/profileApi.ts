// Server-side read of one public profile (CONTRACTS 3.2). Same ladder, timeout and cache as lib/seo/publicApi.ts, which is outside
// this task's files; the PR lists moving this next to it as a follow-up. Never used by the browser.
import { notFound } from "next/navigation";
import { resolveBackend } from "../../lib/api/backends";
import { backendFetch } from "../../lib/api/http";
import { DirectoryUnavailableError, REVALIDATE_SECONDS, type PractitionerCard } from "../../lib/seo/publicApi";

export type Money = { amountMinor: number; currency: "PLN" };
export type Offering = {
  id: string;
  title: string;
  description: string;
  durationMinutes: number;
  price: Money;
  problemSlug: string;
  techSlugs: string[];
};
export type PractitionerProfile = PractitionerCard & {
  bio: string;
  portfolioUrl?: string;
  offerings: Offering[];
  completedCount: number;
  memberSince: string;
};

/** The profile of an approved practitioner. The Directory answers 404 for an unknown slug and for any status other than Approved: both are a 404 page. */
export async function getProfile(slug: string): Promise<PractitionerProfile> {
  const base = await resolveBackend("directory");
  if (!base) throw new DirectoryUnavailableError(0);
  let res: Response;
  try {
    res = await backendFetch(`${base}/api/v1/practitioners/${encodeURIComponent(slug)}`, {
      headers: { accept: "application/json" },
      signal: AbortSignal.timeout(5_000),
      next: { revalidate: REVALIDATE_SECONDS },
    } as RequestInit);
  } catch {
    throw new DirectoryUnavailableError(0);
  }
  if (res.status === 404) notFound();
  if (!res.ok) throw new DirectoryUnavailableError(res.status);
  const profile = (await res.json()) as PractitionerProfile;
  // The contract never sends meetingUrl on a public offering; strip it anyway so a leak upstream cannot reach the HTML.
  return { ...profile, offerings: profile.offerings.map(({ meetingUrl: _m, ...o }: Offering & { meetingUrl?: string }) => o) };
}
