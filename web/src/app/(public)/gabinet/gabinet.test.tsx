import { renderToStaticMarkup } from "react-dom/server";
import { afterEach, describe, expect, it } from "vitest";
import { pf } from "@/components/profile/strings";
import { installProfileDirectory, profile, uninstallProfileDirectory } from "@/components/profile/testFixtures";
import Page, { generateMetadata } from "./[slug]/page";

afterEach(uninstallProfileDirectory);
const count = (s: string, re: RegExp) => (s.match(re) ?? []).length;
const params = (slug: string) => ({ params: Promise.resolve({ slug }) });
const render = async (slug: string) => {
  const el = (await Page(params(slug))) as React.ReactElement<object, (props: object) => Promise<React.ReactElement>>;
  return renderToStaticMarkup(el);
};
const jsonLd = (html: string) => JSON.parse(/<script type="application\/ld\+json">([\s\S]*?)<\/script>/.exec(html)![1].replace(/\\u003c/g, "<"));

describe("profile page without JavaScript", () => {
  it("contains the name, headline, bio, offerings with price and duration, with exactly one h1", async () => {
    const p = profile(1);
    installProfileDirectory(p);
    const html = await render(p.slug);
    expect(html).toContain("demo-Programista 1");
    expect(html).toContain("demo-nagłówek 1");
    expect(html).toContain("demo-bio: kilka zdań");
    expect(html).toContain("demo-Audyt 30");
    expect(html).toContain("150,00");
    expect(html).toContain("30 min");
    expect(html).toContain("250,50");
    expect(count(html, /<h1[\s>]/g)).toBe(1);
  });

  it("links every offering to the booking route, which works without scripts", async () => {
    const p = profile(1);
    installProfileDirectory(p);
    const html = await render(p.slug);
    expect(html).toContain('href="/rezerwacja/00000000-0000-0000-0000-0000000000a1"');
    expect(html).toContain('href="/rezerwacja/00000000-0000-0000-0000-0000000000a2"');
  });

  it("explains each badge accessibly", async () => {
    const p = profile(1, { badges: ["email", "github", "nip", "approved"] });
    installProfileDirectory(p);
    const html = await render(p.slug);
    expect(count(html, /title="[^"]+"/g) + count(html, /aria-describedby/g)).toBeGreaterThanOrEqual(4);
  });

  it("shows no meeting URL, even if the API leaked one", async () => {
    const p = profile(1);
    (p.offerings[0] as unknown as Record<string, unknown>)["meetingUrl"] = "https://meet.demo.example/secret-room";
    installProfileDirectory(p);
    const html = await render(p.slug);
    expect(html).not.toContain("meet.demo.example");
    expect(html).not.toContain("meetingUrl");
  });

  it("renders the reviews block with a heading and the rating summary in the HTML", async () => {
    const p = profile(1, { isNew: false, rating: { average: 4.5, count: 4 } });
    installProfileDirectory(p);
    const html = await render(p.slug);
    expect(html).toContain('data-slot="reviews"');
    expect(html).toContain(pf("reviews.title"));
    expect(html).toContain(pf("reviews.summary", { average: "4,5", count: 4 }));
  });

  it("shows 'Nowy' instead of a summary for a practitioner without reviews", async () => {
    const p = profile(1, { isNew: true, rating: undefined });
    installProfileDirectory(p);
    const html = await render(p.slug);
    expect(html).toContain(pf("reviews.new"));
    expect(html).not.toContain("aggregateRating");
  });

  it("renders the whole profile when Booking is down: the page never calls Booking", async () => {
    const p = profile(1);
    const fake = installProfileDirectory(p);
    const html = await render(p.slug);
    expect(html).toContain("demo-Programista 1");
    expect(fake.requested.every((u) => u.host === "directory.test")).toBe(true);
  });
});

describe("unknown and non-public practitioners", () => {
  it("an unknown or non-approved slug (the Directory answers 404) is a 404 for page and metadata", async () => {
    installProfileDirectory(null);
    await expect(render("demo-suspended")).rejects.toThrow(/NEXT_HTTP_ERROR_FALLBACK;404|NEXT_NOT_FOUND/);
    await expect(generateMetadata(params("demo-suspended"))).rejects.toThrow(/NEXT_HTTP_ERROR_FALLBACK;404|NEXT_NOT_FOUND/);
  });

  it("a failing Directory is an error, not a 404", async () => {
    installProfileDirectory(profile(1), { status: 503 });
    await expect(render("demo-programista-1")).rejects.toThrow(/directory_unavailable:503/);
  });
});

describe("metadata and JSON-LD", () => {
  it("has a title, description, canonical URL and Open Graph, indexable", async () => {
    const p = profile(1);
    installProfileDirectory(p);
    const m = await generateMetadata(params(p.slug));
    expect(String(m.title)).toContain("demo-Programista 1");
    expect(m.description).toContain("demo-nagłówek 1");
    expect(m.alternates?.canonical).toBe("https://gabinety.example/gabinet/demo-programista-1");
    expect(m.openGraph?.title).toBe(m.title);
    expect(m.robots).toEqual({ index: true, follow: true });
  });

  it("encodes the slug in the canonical URL", async () => {
    installProfileDirectory(profile(1, { slug: "demo-jan-kowalski" }));
    expect((await generateMetadata(params("demo-jan-kowalski"))).alternates?.canonical).toBe("https://gabinety.example/gabinet/demo-jan-kowalski");
  });

  it("emits ProfessionalService JSON-LD with the offers and no aggregateRating for a new profile", async () => {
    const p = profile(1);
    installProfileDirectory(p);
    const ld = jsonLd(await render(p.slug));
    expect(ld["@context"]).toBe("https://schema.org");
    expect(ld["@type"]).toBe("ProfessionalService");
    expect(ld.name).toBe("demo-Programista 1");
    expect(ld.url).toBe("https://gabinety.example/gabinet/demo-programista-1");
    expect(ld.aggregateRating).toBeUndefined();
    expect(JSON.stringify(ld)).toContain("PLN");
  });

  it("has no aggregateRating for 2 reviews, even if the Directory sent one, and has it for 3", async () => {
    installProfileDirectory(profile(1, { rating: { average: 4.5, count: 2 }, isNew: true }));
    expect(jsonLd(await render("demo-programista-1")).aggregateRating).toBeUndefined();
    installProfileDirectory(profile(1, { rating: { average: 4.5, count: 3 }, isNew: false }));
    expect(jsonLd(await render("demo-programista-1")).aggregateRating).toEqual({ "@type": "AggregateRating", ratingValue: 4.5, reviewCount: 3, bestRating: 5, worstRating: 1 });
  });

  it("cannot be broken out of the script element by user text", async () => {
    installProfileDirectory(profile(1, { displayName: "demo-</script><img src=x>" }));
    const html = await render("demo-programista-1");
    expect(count(html, /<\/script>/g)).toBe(1);
    expect(html).not.toContain("<img src=x>");
  });
});
