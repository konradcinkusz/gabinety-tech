import { readdirSync, readFileSync, statSync } from "node:fs";
import { join, relative } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";

const srcRoot = fileURLToPath(new URL("..", import.meta.url));
const DIACRITICS = /[ąćęłńóśźżĄĆĘŁŃÓŚŹŻ]/;

function walk(dir: string): string[] {
  return readdirSync(dir).flatMap((n) => {
    const p = join(dir, n);
    return statSync(p).isDirectory() ? walk(p) : [p];
  });
}

// Strips block comments and line comments (not those after a colon, as in URLs),
// keeping line numbers intact.
export function stripComments(src: string): string {
  return src
    .replace(/\/\*[\s\S]*?\*\//g, (m) => m.replace(/[^\n]/g, " "))
    .replace(/(^|[^:"'`])\/\/.*$/gm, "$1");
}

export function polishLines(src: string): number[] {
  return stripComments(src)
    .split("\n")
    .flatMap((line, i) => (DIACRITICS.test(line) ? [i + 1] : []));
}

describe("no Polish literals outside pl.ts", () => {
  const targets = [
    ...walk(join(srcRoot, "components", "ui")),
    ...walk(join(srcRoot, "components", "layout")),
    join(srcRoot, "app", "layout.tsx"),
    join(srcRoot, "app", "not-found.tsx"),
    join(srcRoot, "app", "error.tsx"),
  ].filter((f) => /\.tsx?$/.test(f) && !/\.test\.tsx?$/.test(f));

  it("scans a non-trivial set of files", () => {
    expect(targets.length).toBeGreaterThan(12);
  });

  it("finds none", () => {
    const hits = targets.flatMap((f) => polishLines(readFileSync(f, "utf8")).map((l) => `${relative(srcRoot, f)}:${l}`));
    expect(hits).toEqual([]);
  });

  it("the scanner fails on a deliberate violation and ignores comments", () => {
    expect(polishLines('const a = 1;\nconst b = "Zaloguj się";')).toEqual([2]);
    expect(polishLines("<p>Programiści</p>")).toEqual([1]);
    expect(polishLines("// zażółć\n/* gęślą\n jaźń */\nconst c = 1;")).toEqual([]);
  });
});
