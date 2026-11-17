import { readFileSync, readdirSync } from "node:fs";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";

const here = fileURLToPath(new URL(".", import.meta.url));
const dirs = [here, join(here, "..", "..", "app", "moje", "opinie"), join(here, "..", "..", "app", "moje", "opinie", "[bookingId]")];
const DIACRITICS = /[ąćęłńóśźżĄĆĘŁŃÓŚŹŻ]/;

describe("reviews: no Polish literals outside strings.ts, no logging", () => {
  const files = dirs
    .flatMap((d) => readdirSync(d).map((n) => join(d, n)))
    .filter((f) => /\.tsx?$/.test(f) && !/\.test\.tsx?$/.test(f) && f !== join(here, "strings.ts"));
  it("scans files", () => expect(files.length).toBeGreaterThan(6));
  it("finds no literals", () => {
    const code = (s: string) => s.replace(/\/\*[\s\S]*?\*\//g, "").replace(/(^|[^:"'`])\/\/.*$/gm, "$1");
    expect(files.filter((f) => DIACRITICS.test(code(readFileSync(f, "utf8"))))).toEqual([]);
  });
  it("never logs", () => expect(files.filter((f) => /console\./.test(readFileSync(f, "utf8")))).toEqual([]));
  it("never renders HTML from review text", () =>
    expect(files.filter((f) => /dangerouslySetInnerHTML|innerHTML/.test(readFileSync(f, "utf8")))).toEqual([]));
});
