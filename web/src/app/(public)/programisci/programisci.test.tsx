import { renderToStaticMarkup } from "react-dom/server";
import { afterEach, describe, expect, it } from "vitest";
import { card, installDirectory, sitemapWith, uninstallDirectory } from "@/lib/seo/testFixtures";
import AllPage, { generateMetadata as allMeta } from "./page";
import ProblemPage, { generateMetadata as problemMeta } from "./[problem]/page";
import TechPage, { generateMetadata as techMeta } from "./[problem]/[tech]/page";

afterEach(uninstallDirectory);
const count = (s: string, re: RegExp) => (s.match(re) ?? []).length;
// The page returns an async server component: resolve it (as Next does) and render the result to static markup, no scripts involved.
const render = async (page: Promise<React.ReactElement>) => {
  const el = (await page) as React.ReactElement<object, (props: object) => Promise<React.ReactElement>>;
  return renderToStaticMarkup(await el.type(el.props));
};
const robotsOf = (m: { robots?: unknown }) => m.robots as { index: boolean; follow: boolean };

describe("list pages are server-rendered", () => {
  it("contain the practitioner names in static markup, with one h1", async () => {
    installDirectory({ items: [card(1), card(2), card(3)], sitemap: sitemapWith({ "/programisci": 3 }) });
    const html = await render(AllPage({ searchParams: Promise.resolve({}) }));
    for (const n of [1, 2, 3]) expect(html).toContain(`demo-Programista ${n}`);
    expect(html).toContain('href="/gabinet/demo-programista-1"');
    expect(count(html, /<h1[\s>]/g)).toBe(1);
  });

  it("have exactly one h1 on a category and a category+tech page too", async () => {
    installDirectory({ items: [card(1)], sitemap: sitemapWith({}) });
    const p = await render(ProblemPage({ params: Promise.resolve({ problem: "audyt-kodu" }), searchParams: Promise.resolve({}) }));
    const t = await render(TechPage({ params: Promise.resolve({ problem: "audyt-kodu", tech: "dotnet" }), searchParams: Promise.resolve({}) }));
    expect(count(p, /<h1[\s>]/g)).toBe(1);
    expect(count(t, /<h1[\s>]/g)).toBe(1);
  });

  it("an unknown problem or tech is a 404", async () => {
    installDirectory({ items: [], sitemap: sitemapWith({}) });
    await expect(render(ProblemPage({ params: Promise.resolve({ problem: "nie-ma" }), searchParams: Promise.resolve({}) }))).rejects.toThrow();
    await expect(render(TechPage({ params: Promise.resolve({ problem: "audyt-kodu", tech: "nie-ma" }), searchParams: Promise.resolve({}) }))).rejects.toThrow();
  });
});

describe("indexing", () => {
  const params = Promise.resolve({ problem: "audyt-kodu" });
  const sp = Promise.resolve({});

  it("a category with 2 practitioners is noindex,follow", async () => {
    installDirectory({ items: [card(1), card(2)], sitemap: sitemapWith({ "/programisci/audyt-kodu": 2 }) });
    expect(robotsOf(await problemMeta({ params, searchParams: sp }))).toEqual({ index: false, follow: true });
  });

  it("a category with 3 practitioners is indexable, with a canonical URL and Open Graph", async () => {
    installDirectory({ items: [card(1), card(2), card(3)], sitemap: sitemapWith({ "/programisci/audyt-kodu": 3 }) });
    const m = await problemMeta({ params, searchParams: sp });
    expect(robotsOf(m)).toEqual({ index: true, follow: true });
    expect(m.alternates?.canonical).toBe("https://gabinety.example/programisci/audyt-kodu");
    expect(m.openGraph?.title).toBe(m.title);
    expect(String(m.title)).toContain("Audyt kodu");
    expect(m.description).toBeTruthy();
  });

  it("a category missing from the sitemap (no practitioners) is noindex", async () => {
    installDirectory({ items: [], sitemap: sitemapWith({}) });
    expect(robotsOf(await problemMeta({ params, searchParams: sp }))).toEqual({ index: false, follow: true });
  });

  it("titles and descriptions are unique across the three shapes", async () => {
    installDirectory({ items: [card(1), card(2), card(3)], sitemap: sitemapWith({ "/programisci": 3, "/programisci/audyt-kodu": 3, "/programisci/audyt-kodu/dotnet": 3 }) });
    const a = await allMeta({ searchParams: sp });
    const b = await problemMeta({ params, searchParams: sp });
    const c = await techMeta({ params: Promise.resolve({ problem: "audyt-kodu", tech: "dotnet" }), searchParams: sp });
    expect(new Set([a.title, b.title, c.title]).size).toBe(3);
    expect(new Set([a.description, b.description, c.description]).size).toBe(3);
  });

  it("a filtered or paginated variant is noindex and canonical points at the plain list", async () => {
    installDirectory({ items: [card(1), card(2), card(3)], sitemap: sitemapWith({ "/programisci/audyt-kodu": 3 }) });
    const m = await problemMeta({ params, searchParams: Promise.resolve({ duration: "30" }) });
    expect(robotsOf(m)).toEqual({ index: false, follow: true });
    expect(m.alternates?.canonical).toBe("https://gabinety.example/programisci/audyt-kodu");
  });
});

describe("filters and pagination are plain links", () => {
  it("filter links keep the other filters; next page link carries the cursor and the filters", async () => {
    installDirectory({ items: [card(1)], nextCursor: "CUR2", sitemap: sitemapWith({}) });
    const html = await render(ProblemPage({ params: Promise.resolve({ problem: "audyt-kodu" }), searchParams: Promise.resolve({ duration: "30", maxPriceMinor: "30000" }) }));
    expect(html).toContain('href="/programisci/audyt-kodu/dotnet?duration=30&amp;maxPriceMinor=30000"'); // tech link
    expect(html).toContain('href="/programisci/audyt-kodu?duration=60&amp;maxPriceMinor=30000"'); // duration link
    expect(html).toContain('href="/programisci/integracja-ai?duration=30&amp;maxPriceMinor=30000"'); // other problem
    expect(html).toContain('href="/programisci/audyt-kodu?duration=30&amp;maxPriceMinor=30000&amp;cursor=CUR2"'); // next
  });

  it("forwards the filters to the Directory and passes the cursor through", async () => {
    const fake = installDirectory({ items: [card(1)], sitemap: sitemapWith({}) });
    await render(TechPage({ params: Promise.resolve({ problem: "audyt-kodu", tech: "dotnet" }), searchParams: Promise.resolve({ duration: "60", cursor: "CUR1" }) }));
    const q = fake.requested.find((u) => u.pathname === "/api/v1/practitioners")!.searchParams;
    expect(Object.fromEntries(q)).toMatchObject({ problem: "audyt-kodu", tech: "dotnet", duration: "60", cursor: "CUR1" });
  });
});
