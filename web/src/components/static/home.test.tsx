import { renderToStaticMarkup } from "react-dom/server";
import { afterEach, describe, expect, it } from "vitest";
import { resetBackendMemory } from "@/lib/api/backends";
import { resetBackendFetch, setBackendFetch } from "@/lib/api/http";
import { taxonomy } from "@/lib/seo/testFixtures";
import { HomePage } from "./HomePage";
import { ts } from "./strings";

const count = (s: string, re: RegExp) => (s.match(re) ?? []).length;

function serveTaxonomy(ok: boolean) {
  process.env["Backend__Directory"] = "http://directory.test";
  resetBackendMemory();
  setBackendFetch(async () => (ok ? Response.json(taxonomy) : new Response("{}", { status: 503 })));
}
afterEach(() => resetBackendFetch());

describe("home page", () => {
  it("has exactly one h1", async () => {
    serveTaxonomy(true);
    expect(count(renderToStaticMarkup(await HomePage()), /<h1[\s>]/g)).toBe(1);
  });

  it("has a GET search form to /programisci with a q field that has a label", async () => {
    serveTaxonomy(true);
    const out = renderToStaticMarkup(await HomePage());
    expect(out).toMatch(/<form(?=[^>]*method="get")(?=[^>]*action="\/programisci")[^>]*>/);
    expect(out).toMatch(/<input[^>]*name="q"/);
    expect(out).toMatch(/<input[^>]*maxLength="60"/);
    const labelFor = /<label[^>]*for="([^"]+)"/.exec(out)?.[1];
    expect(labelFor).toBeTruthy();
    expect(out).toContain(`id="${labelFor}"`);
  });

  it("links every problem category to its path", async () => {
    serveTaxonomy(true);
    const out = renderToStaticMarkup(await HomePage());
    expect(out).toContain('href="/programisci/audyt-kodu"');
    expect(out).toContain('href="/programisci/integracja-ai"');
    expect(out).toContain("Audyt kodu");
  });

  it("has the three-step explanation and links to the rules", async () => {
    serveTaxonomy(true);
    const out = renderToStaticMarkup(await HomePage());
    expect(out).toMatch(/<ol[^>]*>(?:(?!<\/ol>)[\s\S])*<\/ol>/);
    const ol = out.match(/<ol[\s\S]*?<\/ol>/)?.[0] ?? "";
    expect(count(ol, /<li[\s>]/g)).toBe(3);
    expect(out).toContain('href="/zasady-rankingu"');
    expect(out).toContain('href="/jak-to-dziala"');
  });

  it("shows an error state in the categories region, not a blank page, when the Directory fails", async () => {
    serveTaxonomy(false);
    const out = renderToStaticMarkup(await HomePage());
    expect(out).toContain(ts("home.categoriesUnavailable"));
    expect(out).toContain('role="alert"');
    expect(count(out, /<h1[\s>]/g)).toBe(1);
    expect(out).toMatch(/<form[^>]*action="\/programisci"/);
  });
});
