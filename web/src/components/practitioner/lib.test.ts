import { describe, expect, it } from "vitest";
import { validateAvailability, issuesFromServer, type Rule } from "./availability";
import { buildChecklist } from "./checklist";
import { groszToInput, parsePricePln } from "./price";

describe("parsePricePln", () => {
  it("250 is 25000 grosz", () => expect(parsePricePln("250")).toEqual({ ok: true, grosz: 25000 }));
  it("250,50 is 25050 grosz", () => expect(parsePricePln("250,50")).toEqual({ ok: true, grosz: 25050 }));
  it("accepts a dot, one decimal and surrounding spaces", () => {
    expect(parsePricePln(" 250.5 ")).toEqual({ ok: true, grosz: 25050 });
  });
  it("rejects 250,555 (three decimals)", () => expect(parsePricePln("250,555")).toEqual({ ok: false, reason: "format" }));
  it.each(["", "abc", "-250", "1e3", "250,", ",50", "2 50", "250 zł"])("rejects %j", (s) => {
    expect(parsePricePln(s)).toMatchObject({ ok: false });
  });
  it("enforces 50 to 1500 PLN (5000 to 150000 grosz)", () => {
    expect(parsePricePln("49,99")).toEqual({ ok: false, reason: "range" });
    expect(parsePricePln("50")).toEqual({ ok: true, grosz: 5000 });
    expect(parsePricePln("1500")).toEqual({ ok: true, grosz: 150000 });
    expect(parsePricePln("1500,01")).toEqual({ ok: false, reason: "range" });
  });
  it("formats grosz back for the input", () => {
    expect(groszToInput(25000)).toBe("250");
    expect(groszToInput(25050)).toBe("250,50");
    expect(groszToInput(25005)).toBe("250,05");
  });
});

const profile = (o: object = {}) => ({
  id: "demo-p", slug: "demo", status: "Draft", displayName: "Demo", headline: "Demo headline ok", bio: "x".repeat(200),
  portfolioUrl: "https://example.test", emailVerified: true, githubLinked: true, nipValid: true, ...o,
});
const off = (isActive: boolean) => ({ id: "o", isActive });

describe("buildChecklist reads only what the API returned", () => {
  const done = (p: object, o: object[]) => Object.fromEntries(buildChecklist(p as never, o as never).map((i) => [i.key, i.done]));
  it("lists the seven submit items in order", () => {
    expect(buildChecklist(profile() as never, [off(true)] as never).map((i) => i.key)).toEqual(["headline", "bio", "offering", "github", "nip", "portfolio", "email"]);
  });
  it("marks everything done for a complete profile", () => {
    expect(Object.values(done(profile(), [off(true)])).every(Boolean)).toBe(true);
  });
  it("bio counts from 200 characters", () => {
    expect(done(profile({ bio: "x".repeat(199) }), [off(true)]).bio).toBe(false);
    expect(done(profile({ bio: "x".repeat(200) }), [off(true)]).bio).toBe(true);
  });
  it("only an ACTIVE offering counts", () => {
    expect(done(profile(), [off(false)]).offering).toBe(false);
    expect(done(profile(), []).offering).toBe(false);
  });
  it("uses the server flags for e-mail, GitHub and NIP, and a filled portfolio", () => {
    expect(done(profile({ emailVerified: false, githubLinked: false, nipValid: false, portfolioUrl: null, headline: "" }), [off(true)]))
      .toEqual({ headline: false, bio: true, offering: true, github: false, nip: false, portfolio: false, email: false });
  });
});

const rule = (isoWeekday: number, startLocal: string, endLocal: string): Rule => ({ isoWeekday, startLocal, endLocal });
const check = (rules: Rule[], exceptions: string[] = []) => validateAvailability({ rules, exceptions: exceptions.map((dateLocal) => ({ dateLocal })) });

describe("validateAvailability (CONTRACTS 4.2, mirrors the Booking API)", () => {
  it("accepts a clean week", () => expect(check([rule(1, "09:00", "12:30"), rule(1, "13:00", "17:00"), rule(7, "10:00", "23:30")])).toEqual([]));
  it("reports an overlap on the same weekday, naming the day", () => {
    expect(check([rule(2, "09:00", "12:00"), rule(2, "11:30", "14:00")])).toEqual([{ code: "overlap", weekday: 2 }]);
  });
  it("touching windows do not overlap; other days do not interact", () => {
    expect(check([rule(2, "09:00", "12:00"), rule(2, "12:00", "14:00"), rule(3, "09:00", "12:00")])).toEqual([]);
  });
  it("end must be after start", () => {
    expect(check([rule(1, "12:00", "12:00")])).toEqual([{ code: "end_before_start", index: 0 }]);
    expect(check([rule(1, "12:00", "09:00")])).toEqual([{ code: "end_before_start", index: 0 }]);
  });
  it("times are on :00 or :30, HH:mm; 24:00 is refused; 23:30 is the latest end", () => {
    expect(check([rule(1, "09:15", "12:00")])).toEqual([{ code: "not_half_hour", index: 0 }]);
    expect(check([rule(1, "09:00", "24:00")])).toEqual([{ code: "not_half_hour", index: 0 }]);
    expect(check([rule(1, "9:00", "12:00")])).toEqual([{ code: "not_half_hour", index: 0 }]);
  });
  it("weekday 1 to 7", () => expect(check([rule(8, "09:00", "10:00")])).toEqual([{ code: "bad_weekday", index: 0 }]));
  it("at most 28 rules and 90 exceptions", () => {
    const many = Array.from({ length: 29 }, (_, i) => rule((i % 7) + 1, `${String(Math.floor(i / 7) * 2).padStart(2, "0")}:00`, `${String(Math.floor(i / 7) * 2 + 1).padStart(2, "0")}:00`));
    expect(check(many.slice(0, 28))).toEqual([]);
    expect(check(many).map((i) => i.code)).toEqual(["too_many_rules"]);
    const days = Array.from({ length: 91 }, (_, i) => new Date(Date.UTC(2027, 0, 1 + i)).toISOString().slice(0, 10));
    expect(check([], days.slice(0, 90))).toEqual([]);
    expect(check([], days).map((i) => i.code)).toEqual(["too_many_exceptions"]);
  });
  it("exception dates are real, unique dates", () => {
    expect(check([], ["2027-02-30"]).map((i) => i.code)).toEqual(["bad_date"]);
    expect(check([], ["2027-03-01", "2027-03-01"]).map((i) => i.code)).toEqual(["duplicate_exception"]);
  });
  it("maps the server's errors back to issues", () => {
    expect(issuesFromServer({ "rules[2].endLocal": ["must be after startLocal"], rules: ["rules overlap on weekday 3"], exceptions: ["dates must be unique"] }))
      .toEqual([{ code: "end_before_start", index: 2 }, { code: "overlap", weekday: 3 }, { code: "duplicate_exception" }]);
    expect(issuesFromServer({ "rules[0].startLocal": ["must be HH:mm on :00 or :30"], rules: ["at most 28 rules"] }))
      .toEqual([{ code: "not_half_hour", index: 0 }, { code: "too_many_rules" }]);
  });
});
