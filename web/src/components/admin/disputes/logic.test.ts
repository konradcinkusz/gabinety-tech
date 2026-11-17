import { describe, expect, it } from "vitest";
import { amountProblem, noteProblem, parsePlnToGrosz } from "./api";

describe("parsePlnToGrosz: exact, no floating point", () => {
  it.each([
    ["12,50", 1250], ["12.50", 1250], ["12", 1200], ["0,07", 7], ["0.7", 70], ["  249,99 ", 24999], ["1,1", 110], ["19.99", 1999], ["0,29", 29],
  ])("%s -> %i", (raw, grosz) => expect(parsePlnToGrosz(raw)).toEqual({ ok: true, grosz }));
  it.each([["12,505"], ["12.999"], [""], ["abc"], ["-5"], ["1 000"], ["1,2,3"], ["12,"], [",5"], ["1e3"], ["99999999999"]])("rejects %j", (raw) =>
    expect(parsePlnToGrosz(raw).ok).toBe(false));
  it("names the reason for too many decimals", () => expect(parsePlnToGrosz("1,234")).toEqual({ ok: false, problem: "decimals" }));
});

describe("amountProblem: strictly between 0 and the price", () => {
  it("accepts 1 to price - 1 grosz", () => {
    expect(amountProblem(1, 10000)).toBeNull();
    expect(amountProblem(9999, 10000)).toBeNull();
  });
  it("rejects 0 and the whole price", () => {
    expect(amountProblem(0, 10000)).toBe("range");
    expect(amountProblem(10000, 10000)).toBe("range");
  });
  it("without a known price only the lower bound is checked (the server checks the rest)", () => {
    expect(amountProblem(0, null)).toBe("range");
    expect(amountProblem(5_000_000, null)).toBeNull();
  });
});

describe("noteProblem: 10 to 500 characters after trimming", () => {
  it.each([["a".repeat(9), "invalid"], ["   " + "a".repeat(9) + "  ", "invalid"], ["a".repeat(10), null], ["a".repeat(500), null], ["a".repeat(501), "invalid"]] as const)(
    "%#", (t, out) => expect(noteProblem(t)).toBe(out));
});
