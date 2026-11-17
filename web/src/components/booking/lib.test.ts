import { describe, expect, it } from "vitest";
import { BRIEF_MAX, BRIEF_MIN, briefProblem, clock, isMockPayments, safeCheckoutTarget } from "./lib";

describe("brief validation (20 to 2000 after trimming)", () => {
  it("rejects 19, accepts 20 and 2000, rejects 2001, counts the trimmed text", () => {
    expect(briefProblem("a".repeat(19))).toBe("short");
    expect(briefProblem("a".repeat(BRIEF_MIN))).toBeNull();
    expect(briefProblem("a".repeat(BRIEF_MAX))).toBeNull();
    expect(briefProblem("a".repeat(BRIEF_MAX + 1))).toBe("long");
    expect(briefProblem("   " + "a".repeat(19) + "   ")).toBe("short");
  });
});

describe("Mock payments gate (SPEC 6.5: fake and mock only)", () => {
  it("is on only for Payments__Provider=Mock or the fake profile", () => {
    expect(isMockPayments({ Payments__Provider: "Mock" })).toBe(true);
    expect(isMockPayments({ Payments__Provider: "mock" })).toBe(true);
    expect(isMockPayments({ GABINETY_FAKE: "1" })).toBe(true);
    expect(isMockPayments({ Payments__Provider: "Stripe" })).toBe(false);
    expect(isMockPayments({ Payments__Provider: "Stripe", GABINETY_FAKE: "1" })).toBe(false);
    expect(isMockPayments({})).toBe(false);
  });
});

describe("checkout target", () => {
  it("keeps same-origin paths, allows https vendors, refuses everything else", () => {
    const o = "http://localhost:3000";
    expect(safeCheckoutTarget("http://localhost:3000/checkout/mock/b1", o)).toBe("/checkout/mock/b1");
    expect(safeCheckoutTarget("/checkout/mock/b1", o)).toBe("/checkout/mock/b1");
    expect(safeCheckoutTarget("https://pay.example.test/c/1", o)).toBe("https://pay.example.test/c/1");
    expect(safeCheckoutTarget("javascript:alert(1)", o)).toBeNull();
    expect(safeCheckoutTarget("http://evil.example.test/x", o)).toBeNull();
    expect(safeCheckoutTarget("//evil.example.test/x", o)).toBeNull();
  });
});

describe("countdown", () => {
  it("formats m:ss and clamps at zero", () => {
    expect(clock(600_000)).toBe("10:00");
    expect(clock(61_000)).toBe("1:01");
    expect(clock(-5)).toBe("0:00");
  });
});
