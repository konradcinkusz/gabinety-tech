import { readdirSync, readFileSync, statSync } from "node:fs";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import { authPath, RETURN_PARAM } from "./returnTo";

const src = fileURLToPath(new URL("../..", import.meta.url));
const walk = (d: string): string[] => readdirSync(d).flatMap((n) => (statSync(join(d, n)).isDirectory() ? walk(join(d, n)) : [join(d, n)]));

describe("sign-in return parameter", () => {
  it("is `redirect`, the one the edge guard, refresh and GitHub handlers read", () => {
    expect(RETURN_PARAM).toBe("redirect");
    expect(authPath("/logowanie", "/moje/opinie/x?a=1")).toBe("/logowanie?redirect=" + encodeURIComponent("/moje/opinie/x?a=1"));
    expect(authPath("/rejestracja", "/")).toBe("/rejestracja");
  });

  it("no producer in web/src builds a `returnTo` query parameter", () => {
    const offenders = walk(src)
      .filter((f) => /\.tsx?$/.test(f) && f !== fileURLToPath(import.meta.url))
      .filter((f) => /[?&]returnTo=|searchParams\.(set|get)\(["']returnTo["']|\bsp\.returnTo\b/.test(readFileSync(f, "utf8")));
    expect(offenders).toEqual([]);
  });
});
