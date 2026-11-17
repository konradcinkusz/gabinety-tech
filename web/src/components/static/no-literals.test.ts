import { readFileSync, readdirSync } from "node:fs";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";

const here = fileURLToPath(new URL(".", import.meta.url));
const app = join(here, "..", "..", "app", "(public)");
const dirs = [
  here,
  app,
  ...["jak-to-dziala", "zasady-rankingu", "cennik", "regulamin", "polityka-prywatnosci"].map((d) => join(app, d)),
];
const DIACRITICS = /[ąćęłńóśźżĄĆĘŁŃÓŚŹŻ]/;

describe("no Polish literals outside strings.ts in the static pages", () => {
  const files = dirs
    .flatMap((d) => readdirSync(d, { withFileTypes: true }).filter((e) => e.isFile()).map((e) => join(d, e.name)))
    .filter((f) => /\.tsx?$/.test(f) && !/\.test\.tsx?$/.test(f) && !/(strings|testFixtures)\.ts$/.test(f));
  it("scans files", () => expect(files.length).toBeGreaterThan(8));
  it("finds none", () => {
    const code = (s: string) => s.replace(/\/\*[\s\S]*?\*\//g, "").replace(/(^|[^:"'`])\/\/.*$/gm, "$1");
    expect(files.filter((f) => DIACRITICS.test(code(readFileSync(f, "utf8"))))).toEqual([]);
  });
});
