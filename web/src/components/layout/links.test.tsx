import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import { Footer } from "./Footer";
import { Header } from "./Header";

describe("layout links point at routes that exist (SPEC 6.5)", () => {
  it("footer links to /regulamin and /polityka-prywatnosci, not /prywatnosc", () => {
    const out = renderToStaticMarkup(<Footer year={2026} />);
    expect(out).toContain('href="/regulamin"');
    expect(out).toContain('href="/polityka-prywatnosci"');
    expect(out).not.toContain('href="/prywatnosc"');
  });
  it("header search link goes to /programisci, not /szukaj", () => {
    const out = renderToStaticMarkup(<Header />);
    expect(out).toContain('href="/programisci"');
    expect(out).not.toContain('href="/szukaj"');
  });
});
