import { readdirSync, readFileSync, statSync } from "node:fs";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import { messageKeys, t } from "./pl";

const srcRoot = fileURLToPath(new URL("..", import.meta.url));

function walk(dir: string): string[] {
  return readdirSync(dir).flatMap((n) => {
    const p = join(dir, n);
    return statSync(p).isDirectory() ? walk(p) : [p];
  });
}

const CALL = /(?<![\w.])t\(\s*(["'`])([\w.-]+)\1/g;

export function usedKeys(source: string): string[] {
  return [...source.matchAll(CALL)].map((m) => m[2]!);
}

describe("catalogue", () => {
  it("every key resolves to a non-empty string", () => {
    expect(messageKeys.length).toBeGreaterThan(30);
    for (const k of messageKeys) expect(t(k).length, k).toBeGreaterThan(0);
  });

  it("every literal t(...) key used in src is defined", () => {
    const defined = new Set<string>(messageKeys);
    const missing: string[] = [];
    for (const f of walk(srcRoot)) {
      if (!/\.tsx?$/.test(f) || /\.test\.tsx?$/.test(f) || f.endsWith(join("i18n", "pl.ts"))) continue;
      for (const k of usedKeys(readFileSync(f, "utf8"))) if (!defined.has(k)) missing.push(`${f}: ${k}`);
    }
    expect(missing).toEqual([]);
  });

  it("the scanner detects a key that is used but not defined", () => {
    const defined = new Set<string>(messageKeys);
    const bad = usedKeys('const x = t("nope.missing"); t("common.save")').filter((k) => !defined.has(k));
    expect(bad).toEqual(["nope.missing"]);
  });
});

describe("t()", () => {
  it("interpolates params", () => {
    expect(t("footer.copyright", { year: 2026 })).toContain("2026");
    expect(t("footer.copyright", { year: 2026 })).not.toContain("{year}");
  });

  it("leaves a missing param placeholder visible", () => {
    expect(t("footer.copyright")).toContain("{year}");
  });
});
