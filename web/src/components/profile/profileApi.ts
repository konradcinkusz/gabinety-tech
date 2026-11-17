// Server-side read of one public profile (CONTRACTS 3.2), through the shared Directory read (timeout, cache, 429 retry). Never used by the browser.
import { notFound } from "next/navigation";
import { DirectoryUnavailableError, directoryGet, type PractitionerCard } from "../../lib/seo/publicApi";

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
  const res = await directoryGet(`/api/v1/practitioners/${encodeURIComponent(slug)}`);
  if (res.status === 404) notFound();
  if (!res.ok) throw new DirectoryUnavailableError(res.status);
  const profile = (await res.json()) as PractitionerProfile;
  // The contract never sends meetingUrl on a public offering; strip it anyway so a leak upstream cannot reach the HTML.
  return { ...profile, offerings: profile.offerings.map(({ meetingUrl: _m, ...o }: Offering & { meetingUrl?: string }) => o) };
}
