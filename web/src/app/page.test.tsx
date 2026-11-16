import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import RootLayout from "./layout";
import Home from "./page";

const count = (s: string, re: RegExp) => (s.match(re) ?? []).length;

describe("home page inside the root layout", () => {
  const out = renderToStaticMarkup(
    <RootLayout>
      <Home />
    </RootLayout>,
  );

  it("has exactly one main landmark (the layout's), not a main nested in a main", () => {
    expect(count(out, /<main[\s>]/g)).toBe(1);
  });

  it("has exactly one h1", () => {
    expect(count(out, /<h1[\s>]/g)).toBe(1);
  });
});
