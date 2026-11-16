import { describe, expect, it } from "vitest";
import { formatPln } from "./format";

const NBSP = " ";

describe("formatPln", () => {
  it.each([
    [25000, `250,00${NBSP}zł`],
    [1, `0,01${NBSP}zł`],
    [0, `0,00${NBSP}zł`],
    [5000, `50,00${NBSP}zł`],
    [150000, `1${NBSP}500,00${NBSP}zł`],
    [100, `1,00${NBSP}zł`],
    [99, `0,99${NBSP}zł`],
    [123456789, `1${NBSP}234${NBSP}567,89${NBSP}zł`],
  ])("formats %i grosz", (grosz, expected) => {
    expect(formatPln(grosz)).toBe(expected);
  });

  it("rejects negative and non-integer amounts", () => {
    expect(() => formatPln(-1)).toThrow(RangeError);
    expect(() => formatPln(1.5)).toThrow(RangeError);
    expect(() => formatPln(Number.NaN)).toThrow(RangeError);
  });
});
