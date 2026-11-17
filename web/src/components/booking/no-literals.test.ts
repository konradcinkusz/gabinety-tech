import { readFileSync, readdirSync } from "node:fs";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";

const here = fileURLToPath(new URL(".", import.meta.url));
const dirs = [here, join(here, "..", "..", "app", "checkout", "mock", "[bookingId]"), join(here, "..", "..", "app", "rezerwacja", "[offeringId]"), join(here, "..", "..", "app", "rezerwacja", "potwierdzenie", "[bookingId]")];
const DIACRITICS = /[ąćęłńóśźżĄĆĘŁŃÓŚŹŻ]/;

describe("no Polish literals outside strings.ts in the booking flow", () => {
  const files = dirs
    .flatMap((d) => readdirSync(d).map((n) => join(d, n)))
    .filter((f) => /\.tsx?$/.test(f) && !/\.test\.tsx?$/.test(f) && !/(strings)\.ts$/.test(f));
  it("scans files", () => expect(files.length).toBeGreaterThan(8));
  it("finds none", () => {
    const code = (s: string) => s.replace(/\/\*[\s\S]*?\*\//g, "").replace(/(^|[^:"'`])\/\/.*$/gm, "$1");
    expect(files.filter((f) => DIACRITICS.test(code(readFileSync(f, "utf8"))))).toEqual([]);
  });
});
