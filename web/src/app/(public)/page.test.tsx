import { renderToStaticMarkup } from "react-dom/server";
import { afterEach, describe, expect, it } from "vitest";
import { resetBackendMemory } from "@/lib/api/backends";
import { resetBackendFetch, setBackendFetch } from "@/lib/api/http";
import { taxonomy } from "@/lib/seo/testFixtures";
import RootLayout from "../layout";
import Home from "./page";

const count = (s: string, re: RegExp) => (s.match(re) ?? []).length;

afterEach(() => resetBackendFetch());

describe("home page inside the root layout", async () => {
  process.env["Backend__Directory"] = "http://directory.test";
  resetBackendMemory();
  setBackendFetch(async () => Response.json(taxonomy));
  const out = renderToStaticMarkup(<RootLayout>{await Home()}</RootLayout>);

  it("has exactly one main landmark (the layout's), not a main nested in a main", () => {
    expect(count(out, /<main[\s>]/g)).toBe(1);
  });

  it("has exactly one h1", () => {
    expect(count(out, /<h1[\s>]/g)).toBe(1);
  });

  it("links the categories to their paths", () => {
    expect(out).toContain('href="/programisci/audyt-kodu"');
  });
});
