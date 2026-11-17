import { readFileSync, readdirSync } from "node:fs";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";

const here = fileURLToPath(new URL(".", import.meta.url));
const dirs = [here, join(here, "..", "..", "app", "moje", "rezerwacje"), join(here, "..", "..", "app", "moje", "rezerwacje", "[id]")];
const DIACRITICS = /[ąćęłńóśźżĄĆĘŁŃÓŚŹŻ]/;

describe("no Polish literals outside strings.ts in my bookings", () => {
  const files = dirs
    .flatMap((d) => readdirSync(d).map((n) => join(d, n)))
    .filter((f) => /\.tsx?$/.test(f) && !/\.test\.tsx?$/.test(f) && f !== join(here, "strings.ts"));
  it("scans files", () => expect(files.length).toBeGreaterThan(6));
  it("finds none", () => {
    const code = (s: string) => s.replace(/\/\*[\s\S]*?\*\//g, "").replace(/(^|[^:"'`])\/\/.*$/gm, "$1");
    expect(files.filter((f) => DIACRITICS.test(code(readFileSync(f, "utf8"))))).toEqual([]);
  });
  it("never logs", () => {
    const logs = files.filter((f) => /console\./.test(readFileSync(f, "utf8")));
    expect(logs).toEqual([]);
  });
});
