import { describe, expect, it } from "vitest";
import { safeReturnTo } from "./returnTo";

describe("safeReturnTo", () => {
  it("keeps a same-site path with query and hash", () => {
    expect(safeReturnTo("/programisci?q=demo#x")).toBe("/programisci?q=demo#x");
  });
  it.each([
    "https://evil.example",
    "http://evil.example/x",
    "//evil.example",
    "/\\evil.example",
    "/\t/evil.example",
    "javascript:alert(1)",
    "evil.example",
    "/api/auth/logout",
    "",
    null,
    undefined,
  ])("falls back to / for %j", (v) => {
    expect(safeReturnTo(v as string | null | undefined)).toBe("/");
  });
  it("never returns the sign-in pages themselves (no loop)", () => {
    expect(safeReturnTo("/logowanie?redirect=/x")).toBe("/");
    expect(safeReturnTo("/rejestracja")).toBe("/");
  });
});
