// Synthetic data for tests (names carry the demo- prefix). Not imported by production code.
import { resetBackendMemory } from "../api/backends";
import { resetBackendFetch, setBackendFetch } from "../api/http";
import type { PractitionerCard, SitemapDto, TaxonomyDto } from "./publicApi";

export const taxonomy: TaxonomyDto = {
  version: "2026-11-v1",
  problems: [
    { slug: "audyt-kodu", namePl: "Audyt kodu" },
    { slug: "integracja-ai", namePl: "Integracja AI" },
  ],
  techs: [
    { slug: "dotnet", namePl: ".NET" },
    { slug: "python", namePl: "Python" },
  ],
};

export const card = (n: number): PractitionerCard => ({
  id: `00000000-0000-0000-0000-00000000000${n}`,
  slug: `demo-programista-${n}`,
  displayName: `demo-Programista ${n}`,
  headline: `demo-nagłówek ${n}`,
  badges: ["email", "approved"],
  isNew: true,
  fromPrice: { amountMinor: 25000 + n, currency: "PLN" },
  problemSlugs: ["audyt-kodu"],
  durations: [30, 60],
});

export const sitemapWith = (counts: Record<string, number>): SitemapDto => ({
  practitioners: [{ slug: "demo-programista-1", lastModified: "2026-11-01T10:00:00Z" }],
  categories: Object.entries(counts).map(([path, practitionerCount]) => ({ path, practitionerCount, indexable: practitionerCount >= 3 })),
});

export type FakeDirectory = { items: PractitionerCard[]; nextCursor?: string; sitemap: SitemapDto; requested: URL[] };

/** Installs a fake Directory behind backendFetch and points the config at it. */
export function installDirectory(init: Omit<FakeDirectory, "requested">): FakeDirectory {
  process.env["Backend__Directory"] = "http://directory.test";
  process.env["Site__BaseUrl"] = "https://gabinety.example";
  resetBackendMemory();
  const fake: FakeDirectory = { ...init, requested: [] };
  setBackendFetch(async (input) => {
    const url = new URL(String(input));
    fake.requested.push(url);
    const json = (body: unknown) => Response.json(body);
    if (url.pathname === "/api/v1/taxonomy") return json(taxonomy);
    if (url.pathname === "/api/v1/sitemap") return json(fake.sitemap);
    if (url.pathname === "/api/v1/practitioners") return json({ items: fake.items, ...(fake.nextCursor ? { nextCursor: fake.nextCursor } : {}) });
    return new Response("{}", { status: 404 });
  });
  return fake;
}

export const uninstallDirectory = () => {
  resetBackendFetch();
  resetBackendMemory();
  delete process.env["Backend__Directory"];
  delete process.env["Site__BaseUrl"];
};
