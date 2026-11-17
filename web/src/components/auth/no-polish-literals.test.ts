import { readdirSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import { polishLines } from "../../i18n/no-polish-literals.test";

const src = fileURLToPath(new URL("../..", import.meta.url));
const dirs = [join(src, "components", "auth"), join(src, "app", "(auth)")];
const walk = (d: string): string[] =>
  readdirSync(d, { withFileTypes: true }).flatMap((e) => (e.isDirectory() ? walk(join(d, e.name)) : [join(d, e.name)]));

describe("auth pages carry no Polish literals outside strings.ts", () => {
  const files = dirs.flatMap(walk).filter((f) => /\.tsx?$/.test(f) && !/\.test\.tsx?$/.test(f) && !f.endsWith("strings.ts"));
  it("scans the pages and components", () => expect(files.length).toBeGreaterThan(6));
  it("finds none", () => {
    expect(files.flatMap((f) => polishLines(readFileSync(f, "utf8")).map((l) => `${f}:${l}`))).toEqual([]);
  });
});
