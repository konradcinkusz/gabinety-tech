// Synthetic profile data for tests (demo- prefix). Not imported by production code.
import { resetBackendMemory } from "../../lib/api/backends";
import { resetBackendFetch, setBackendFetch } from "../../lib/api/http";
import { card } from "../../lib/seo/testFixtures";
import type { PractitionerProfile } from "./profileApi";

export const profile = (n = 1, over: Partial<PractitionerProfile> = {}): PractitionerProfile => ({
  ...card(n),
  bio: "demo-bio: kilka zdań o doświadczeniu.\nDruga linia.",
  portfolioUrl: "https://demo.example/portfolio",
  githubLogin: "demo-gh",
  completedCount: 12,
  memberSince: "2026-05-01T00:00:00Z",
  offerings: [
    { id: "00000000-0000-0000-0000-0000000000a1", title: "demo-Audyt 30", description: "demo-opis audytu", durationMinutes: 30, price: { amountMinor: 15000, currency: "PLN" }, problemSlug: "audyt-kodu", techSlugs: ["dotnet"] },
    { id: "00000000-0000-0000-0000-0000000000a2", title: "demo-Audyt 60", description: "demo-opis dłuższy", durationMinutes: 60, price: { amountMinor: 25050, currency: "PLN" }, problemSlug: "audyt-kodu", techSlugs: [] },
  ],
  ...over,
});

/** Fake Directory serving one profile (status: what the Directory answers for that slug). */
export function installProfileDirectory(p: PractitionerProfile | null, opts: { status?: number } = {}) {
  process.env["Backend__Directory"] = "http://directory.test";
  process.env["Site__BaseUrl"] = "https://gabinety.example";
  resetBackendMemory();
  const requested: URL[] = [];
  setBackendFetch(async (input) => {
    const url = new URL(String(input));
    requested.push(url);
    if (opts.status && opts.status !== 200) return new Response("{}", { status: opts.status });
    if (p && url.pathname === `/api/v1/practitioners/${p.slug}`) return Response.json(p);
    return new Response("{}", { status: 404 });
  });
  return { requested };
}

export const uninstallProfileDirectory = () => {
  resetBackendFetch();
  resetBackendMemory();
  delete process.env["Backend__Directory"];
  delete process.env["Site__BaseUrl"];
};
