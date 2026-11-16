import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";

const css = readFileSync(new URL("./tokens.css", import.meta.url), "utf8");

function tokens(): Map<string, string> {
  const map = new Map<string, string>();
  for (const m of css.matchAll(/(--[\w-]+)\s*:\s*([^;]+);/g)) map.set(m[1]!, m[2]!.trim());
  return map;
}

function luminance(hex: string): number {
  const h = /^#([0-9a-f]{6})$/i.exec(hex)?.[1];
  if (!h) throw new Error(`not a 6-digit hex colour: ${hex}`);
  const [r, g, b] = [0, 2, 4].map((i) => {
    const c = parseInt(h.slice(i, i + 2), 16) / 255;
    return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
  }) as [number, number, number];
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

export function contrast(a: string, b: string): number {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x) as [number, number];
  return (hi + 0.05) / (lo + 0.05);
}

// [foreground token, background token, minimum ratio]
const PAIRS: ReadonlyArray<readonly [string, string, number]> = [
  ["--color-text", "--color-bg", 4.5],
  ["--color-text", "--color-surface", 4.5],
  ["--color-muted", "--color-bg", 4.5],
  ["--color-muted", "--color-surface", 4.5],
  ["--color-accent", "--color-bg", 4.5],
  ["--color-accent-text", "--color-accent", 4.5],
  ["--color-danger", "--color-danger-bg", 4.5],
  ["--color-danger", "--color-bg", 4.5],
  ["--color-danger-text", "--color-danger", 4.5],
  ["--color-success", "--color-success-bg", 4.5],
  ["--color-warning", "--color-warning-bg", 4.5],
  ["--color-focus", "--color-bg", 3],
  ["--color-border-strong", "--color-bg", 3],
];

describe("design tokens", () => {
  const map = tokens();

  it.each(PAIRS)("%s on %s is at least %d:1", (fg, bg, min) => {
    expect(map.has(fg), `${fg} missing`).toBe(true);
    expect(map.has(bg), `${bg} missing`).toBe(true);
    expect(contrast(map.get(fg)!, map.get(bg)!)).toBeGreaterThanOrEqual(min);
  });

  it("declares the scales", () => {
    for (let i = 1; i <= 8; i++) expect(map.has(`--space-${i}`)).toBe(true);
    for (const s of ["sm", "md", "lg", "xl", "2xl"]) expect(map.has(`--font-size-${s}`)).toBe(true);
    expect(map.has("--focus-ring")).toBe(true);
    expect(map.get("--focus-ring")).toContain("2px");
  });

  it("is light only", () => {
    expect(css).not.toContain("prefers-color-scheme");
  });
});
