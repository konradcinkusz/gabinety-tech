import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import RootLayout from "./layout";
import NotFound from "./not-found";
import ErrorPage from "./error";

const count = (s: string, re: RegExp) => (s.match(re) ?? []).length;

describe("root layout", () => {
  const out = renderToStaticMarkup(
    <RootLayout>
      <h1>page</h1>
    </RootLayout>,
  );

  it("is Polish and has exactly one skip link before the header", () => {
    expect(out).toContain('<html lang="pl"');
    expect(count(out, /href="#main"/g)).toBe(1);
    expect(out.indexOf('href="#main"')).toBeLessThan(out.indexOf("<header"));
    expect(out.indexOf("<a ")).toBe(out.indexOf('<a href="#main"'));
  });

  it("has main#main, header and footer", () => {
    expect(out).toMatch(/<main id="main" tabindex="-1"/);
    expect(out).toContain("<header");
    expect(out).toContain("<footer");
    expect(count(out, /<h1[ >]/g)).toBe(1);
  });
});

describe("not-found and error pages", () => {
  it("not-found has one h1 and a link home", () => {
    const out = renderToStaticMarkup(<NotFound />);
    expect(count(out, /<h1[ >]/g)).toBe(1);
    expect(out).toContain('href="/"');
  });

  it("error has one h1 and a retry button", () => {
    const out = renderToStaticMarkup(<ErrorPage error={new Error("x")} retry={() => {}} reset={() => {}} />);
    expect(count(out, /<h1[ >]/g)).toBe(1);
    expect(out).toContain("<button");
  });
});
