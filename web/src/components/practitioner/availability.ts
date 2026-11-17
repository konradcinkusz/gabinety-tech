// Mirrors the Booking API's validation (CONTRACTS 4.2) so the form refuses what the server would. The server stays the authority.
export type Rule = { isoWeekday: number; startLocal: string; endLocal: string };
export type Availability = { rules: Rule[]; exceptions: { dateLocal: string }[] };
export type IssueCode = "too_many_rules" | "too_many_exceptions" | "not_half_hour" | "end_before_start" | "overlap" | "duplicate_exception" | "bad_weekday" | "bad_date";
export type Issue = { code: IssueCode; index?: number; weekday?: number };

export const MAX_RULES = 28;
export const MAX_EXCEPTIONS = 90;

const TIME = /^([01]\d|2[0-3]):(00|30)$/; // 24:00 is not expressible: the latest time is 23:30
const minutes = (t: string) => Number(t.slice(0, 2)) * 60 + Number(t.slice(3));

/** Every :00 and :30 from 00:00 to 23:30, the values a time select offers. */
export const TIMES: string[] = Array.from({ length: 48 }, (_, i) => `${String(Math.floor(i / 2)).padStart(2, "0")}:${i % 2 ? "30" : "00"}`);

function realDate(s: string): boolean {
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(s);
  if (!m) return false;
  const d = new Date(Date.UTC(+m[1], +m[2] - 1, +m[3]));
  return d.getUTCFullYear() === +m[1] && d.getUTCMonth() === +m[2] - 1 && d.getUTCDate() === +m[3];
}

export function validateAvailability(a: Availability): Issue[] {
  const issues: Issue[] = [];
  if (a.rules.length > MAX_RULES) issues.push({ code: "too_many_rules" });
  if (a.exceptions.length > MAX_EXCEPTIONS) issues.push({ code: "too_many_exceptions" });
  const usable: { day: number; start: number; end: number }[] = [];
  a.rules.forEach((r, index) => {
    let ok = true;
    if (!Number.isInteger(r.isoWeekday) || r.isoWeekday < 1 || r.isoWeekday > 7) { issues.push({ code: "bad_weekday", index }); ok = false; }
    if (!TIME.test(r.startLocal) || !TIME.test(r.endLocal)) { issues.push({ code: "not_half_hour", index }); ok = false; }
    else if (minutes(r.endLocal) <= minutes(r.startLocal)) { issues.push({ code: "end_before_start", index }); ok = false; }
    if (ok) usable.push({ day: r.isoWeekday, start: minutes(r.startLocal), end: minutes(r.endLocal) });
  });
  const byDay = Map.groupBy(usable, (r) => r.day);
  for (const [weekday, rows] of [...byDay].sort((x, y) => x[0] - y[0])) {
    const sorted = [...rows].sort((x, y) => x.start - y.start);
    if (sorted.some((r, i) => i > 0 && r.start < sorted[i - 1].end)) issues.push({ code: "overlap", weekday });
  }
  const dates = a.exceptions.map((e) => e.dateLocal);
  if (dates.some((d) => !realDate(d))) issues.push({ code: "bad_date" });
  else if (new Set(dates).size !== dates.length) issues.push({ code: "duplicate_exception" });
  return issues;
}

/** Turns the Booking API's `errors` map (keys like `rules[2].endLocal`, `rules`, `exceptions`) back into issues. */
export function issuesFromServer(errors: Record<string, string[]>): Issue[] {
  const out: Issue[] = [];
  for (const [key, messages] of Object.entries(errors)) {
    for (const message of messages) {
      const row = /^rules\[(\d+)\]/.exec(key);
      const index = row ? Number(row[1]) : undefined;
      if (row && /isoWeekday/.test(key)) out.push({ code: "bad_weekday", index });
      else if (row && /after startLocal/.test(message)) out.push({ code: "end_before_start", index });
      else if (row) out.push({ code: "not_half_hour", index });
      else if (key === "rules" && /overlap on weekday (\d)/.test(message)) out.push({ code: "overlap", weekday: Number(/weekday (\d)/.exec(message)![1]) });
      else if (key === "rules") out.push({ code: "too_many_rules" });
      else if (key === "exceptions" && /unique/.test(message)) out.push({ code: "duplicate_exception" });
      else if (key === "exceptions") out.push({ code: "too_many_exceptions" });
    }
  }
  return out;
}
