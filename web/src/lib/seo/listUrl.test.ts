import { describe, expect, it } from "vitest";
import { listHref, parseListState, withFilter } from "./listUrl";

describe("list state and links", () => {
  it("parses the filters and drops invalid ones", () => {
    const s = parseListState({ problem: "audyt-kodu" }, { duration: "60", maxPriceMinor: "30000", q: " ab ", cursor: "c1" });
    expect(s).toEqual({ problem: "audyt-kodu", duration: 60, maxPriceMinor: 30000, q: "ab", cursor: "c1" });
    const bad = parseListState({}, { duration: "45", maxPriceMinor: "-3", q: "x".repeat(61) });
    expect(bad).toEqual({});
  });

  it("builds the three URL shapes", () => {
    expect(listHref({})).toBe("/programisci");
    expect(listHref({ problem: "audyt-kodu" })).toBe("/programisci/audyt-kodu");
    expect(listHref({ problem: "audyt-kodu", tech: "dotnet" })).toBe("/programisci/audyt-kodu/dotnet");
    expect(listHref({ tech: "dotnet" })).toBe("/programisci"); // a tech never stands alone in a URL
  });

  it("filter links preserve every other filter and reset the cursor", () => {
    const base = { problem: "audyt-kodu", duration: 30 as const, maxPriceMinor: 30000, q: "azure", cursor: "c9" };
    expect(withFilter(base, { tech: "dotnet" })).toEqual({ problem: "audyt-kodu", tech: "dotnet", duration: 30, maxPriceMinor: 30000, q: "azure" });
    expect(listHref(withFilter(base, { duration: 60 }))).toBe("/programisci/audyt-kodu?duration=60&maxPriceMinor=30000&q=azure");
    expect(listHref(withFilter(base, { maxPriceMinor: undefined }))).toBe("/programisci/audyt-kodu?duration=30&q=azure");
  });

  it("changing the problem drops the tech", () => {
    const s = withFilter({ problem: "audyt-kodu", tech: "dotnet", duration: 30 }, { problem: "integracja-ai" });
    expect(listHref(s)).toBe("/programisci/integracja-ai?duration=30");
  });

  it("escapes the free text in a link", () => {
    expect(listHref({ q: "a&b c" })).toBe("/programisci?q=a%26b+c");
  });
});
