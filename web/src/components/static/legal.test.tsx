import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import { LegalPage } from "./LegalPage";
import { loadLegal, parseLegal, renderBlocks } from "./legal";
import { ts } from "./strings";

const sample = `---
version: 0.1-test
date: 2026-11-01
draft: true
---
# Tytul
Akapit z <script>alert(1)</script> i **pogrubieniem**.

## Sekcja
- jeden
- dwa

1. pierwszy
2. drugi
`;

describe("parseLegal", () => {
  it("reads the front matter and keeps the body", () => {
    const d = parseLegal(sample);
    expect(d).toMatchObject({ version: "0.1-test", date: "2026-11-01", draft: true });
    expect(d.body).toMatch(/^# Tytul/);
  });
  it("rejects a file without version and date", () => {
    expect(() => parseLegal("# no front matter")).toThrow();
    expect(() => parseLegal("---\nversion: 1\n---\nx")).toThrow();
  });
});

describe("renderBlocks", () => {
  const out = renderToStaticMarkup(<>{renderBlocks(parseLegal(sample).body)}</>);
  it("renders headings, paragraphs and lists as elements", () => {
    expect(out).toContain("<h3>Sekcja</h3>");
    expect(out).toMatch(/<ul>\s*<li>jeden<\/li>\s*<li>dwa<\/li>\s*<\/ul>/);
    expect(out).toMatch(/<ol>\s*<li>pierwszy<\/li>\s*<li>drugi<\/li>\s*<\/ol>/);
  });
  it("renders text as text: markup in the file is escaped", () => {
    expect(out).not.toContain("<script>");
    expect(out).toContain("&lt;script&gt;");
  });
  it("demotes the file's # heading below the page h1 (one h1 per page)", () => {
    expect(out).not.toContain("<h1");
    expect(out).toContain("<h2>Tytul</h2>");
  });
});

describe("the owner's files", () => {
  it.each(["regulamin", "polityka-prywatnosci"] as const)("%s has a version, a date and a draft marker until T-10.02", (name) => {
    const d = loadLegal(name);
    expect(d.version).toBeTruthy();
    expect(d.date).toMatch(/^\d{4}-\d{2}-\d{2}$/);
    expect(d.draft).toBe(true);
  });
});

describe("LegalPage", () => {
  it("shows the draft notice, the version and the date, with one h1", () => {
    const out = renderToStaticMarkup(<LegalPage kind="terms" doc={parseLegal(sample)} />);
    expect(out).toContain(ts("legal.draftNotice"));
    expect(out).toContain("0.1-test");
    expect(out).toContain("01.11.2026");
    expect((out.match(/<h1[\s>]/g) ?? []).length).toBe(1);
  });
  it("omits the draft notice once the owner's file is final", () => {
    const out = renderToStaticMarkup(<LegalPage kind="privacy" doc={{ ...parseLegal(sample), draft: false }} />);
    expect(out).not.toContain(ts("legal.draftNotice"));
  });
});
