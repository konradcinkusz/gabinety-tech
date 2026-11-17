import { describe, expect, it } from "vitest";
import { dayHeading, groupByWarsawDay, slotWindow, warsawDate, warsawTime, withinHorizon, HORIZON_DAYS, WINDOW_DAYS } from "./slotTime";

const slot = (startUtc: string) => ({ startUtc, endUtc: startUtc });

describe("times are shown in Europe/Warsaw", () => {
  it("2027-03-28T01:30:00Z is 03:30 (the spring change happens at 01:00Z)", () => {
    expect(warsawTime("2027-03-28T01:30:00Z")).toBe("03:30");
  });
  it("just before the change the offset is +1", () => {
    expect(warsawTime("2027-03-28T00:30:00Z")).toBe("01:30");
  });
  it("2027-10-31T01:30:00Z is 02:30 (the autumn change happens at 01:00Z)", () => {
    expect(warsawTime("2027-10-31T01:30:00Z")).toBe("02:30");
  });
  it("the local date follows Warsaw, not UTC", () => {
    expect(warsawDate("2027-03-27T23:30:00Z")).toBe("2027-03-28"); // 00:30 local
    expect(warsawDate("2027-07-01T21:59:00Z")).toBe("2027-07-01");
    expect(warsawDate("2027-07-01T22:00:00Z")).toBe("2027-07-02");
  });
  it("groups across a DST change by local date, in time order", () => {
    const g = groupByWarsawDay([
      slot("2027-03-28T08:00:00Z"),
      slot("2027-03-27T23:30:00Z"),
      slot("2027-03-27T09:00:00Z"),
      slot("2027-03-28T01:30:00Z"),
    ]);
    expect(g.map((d) => [d.date, d.slots.map((s) => warsawTime(s.startUtc))])).toEqual([
      ["2027-03-27", ["10:00"]],
      ["2027-03-28", ["00:30", "03:30", "10:00"]],
    ]);
  });
  it("a day heading is Polish text for the local date", () => {
    expect(dayHeading("2027-03-28")).toMatch(/28 marca/);
  });
});

describe("horizon and windows", () => {
  const now = new Date("2027-03-01T10:00:00Z"); // 11:00 in Warsaw
  it("a window is at most 14 days and never ends after the 60 day horizon", () => {
    for (let i = 0; i < 6; i++) {
      const w = slotWindow(now, i);
      if (!w) continue;
      const days = (Date.parse(w.to) - Date.parse(w.from)) / 86_400_000 + 1;
      expect(days).toBeLessThanOrEqual(WINDOW_DAYS);
      expect(WINDOW_DAYS).toBeLessThanOrEqual(14);
      expect(Date.parse(w.to)).toBeLessThanOrEqual(Date.parse("2027-03-01") + HORIZON_DAYS * 86_400_000);
    }
  });
  it("windows are contiguous, start today (Warsaw date) and run out", () => {
    const w0 = slotWindow(now, 0)!;
    const w1 = slotWindow(now, 1)!;
    expect(w0.from).toBe("2027-03-01");
    expect(Date.parse(w1.from) - Date.parse(w0.to)).toBe(86_400_000);
    expect(slotWindow(now, 10)).toBeNull();
  });
  it("the Warsaw date, not the UTC one, starts the first window", () => {
    expect(slotWindow(new Date("2027-03-01T23:30:00Z"), 0)!.from).toBe("2027-03-02");
  });
  it("drops every slot after the horizon and keeps the ones before", () => {
    const last = slotWindow(now, 4)!.to; // last offered date
    const inside = slot(`${last}T09:00:00Z`);
    const after = slot("2027-05-31T09:00:00Z");
    expect(withinHorizon([inside, after], now)).toEqual([inside]);
  });
  it("drops slots that already started", () => {
    expect(withinHorizon([slot("2027-03-01T09:00:00Z"), slot("2027-03-01T11:00:00Z")], now)).toEqual([slot("2027-03-01T11:00:00Z")]);
  });
});
