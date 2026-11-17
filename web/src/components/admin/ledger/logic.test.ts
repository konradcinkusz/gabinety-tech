import { describe, expect, it } from "vitest";
import { MAX_DAYS, csvHref, currentMonth, dayNumber, periodOf, presets, problemOf } from "./api";

const NOW = new Date("2026-11-17T10:00:00Z");

describe("dayNumber: real UTC dates only", () => {
  it.each(["2026-02-30", "2026-13-01", "2026-1-01", "20260101", "", "2026-01-01T00:00:00Z", "abc"])("rejects %j", (s) => expect(dayNumber(s)).toBeNull());
  it("accepts a leap day and counts consecutive days", () => {
    expect(dayNumber("2028-02-29")).not.toBeNull();
    expect(dayNumber("2026-03-01")! - dayNumber("2026-02-28")!).toBe(1);
  });
});

describe("problemOf: the limits of the endpoint", () => {
  it("accepts one day and 366 days counting both ends", () => {
    expect(problemOf("2026-05-05", "2026-05-05")).toBeNull();
    expect(problemOf("2028-01-01", "2028-12-31")).toBeNull(); // leap year: 366 days
    expect(MAX_DAYS).toBe(366);
  });
  it("refuses 367 days, a reversed range and unparsable days", () => {
    expect(problemOf("2026-01-01", "2027-01-02")).toBe("range");
    expect(problemOf("2026-05-06", "2026-05-05")).toBe("order");
    expect(problemOf("", "2026-05-05")).toBe("format");
  });
});

describe("periodOf", () => {
  it("defaults to the current month so far, in UTC", () => expect(periodOf({}, NOW)).toEqual({ from: "2026-11-01", to: "2026-11-17", problem: null }));
  it("takes the query as given and names the problem", () => {
    expect(periodOf({ from: "2026-01-01", to: "2026-03-31" }, NOW)).toEqual({ from: "2026-01-01", to: "2026-03-31", problem: null });
    expect(periodOf({ from: "2026-01-01" }, NOW).problem).toBe("format");
    expect(periodOf({ from: ["x"], to: 3 }, NOW).problem).toBe("format");
  });
  it("uses the UTC day near midnight", () => expect(currentMonth(new Date("2026-12-31T23:30:00Z")).to).toBe("2026-12-31"));
});

describe("presets and the link", () => {
  it("this month, last month (across a year end too) and this year", () => {
    expect(presets(NOW).map((p) => [p.id, p.from, p.to])).toEqual([
      ["thisMonth", "2026-11-01", "2026-11-17"], ["lastMonth", "2026-10-01", "2026-10-31"], ["thisYear", "2026-01-01", "2026-11-17"],
    ]);
    const jan = presets(new Date("2027-01-10T00:00:00Z"));
    expect([jan[1].from, jan[1].to]).toEqual(["2026-12-01", "2026-12-31"]);
  });
  it("every preset is a valid period", () => expect(presets(new Date("2028-12-31T00:00:00Z")).map((p) => problemOf(p.from, p.to))).toEqual([null, null, null]));
  it("the CSV link carries the dates", () => expect(csvHref("2026-01-01", "2026-03-31")).toBe("/admin/ksiega/csv?from=2026-01-01&to=2026-03-31"));
});
