// Period rules of the Booking ledger export (CONTRACTS 4.3): `GET /api/v1/admin/ledger.csv?from=yyyy-MM-dd&to=yyyy-MM-dd`, declared by hand
// (the generated types of T-07.01 do not know /api/v1/admin). Days are UTC days, both ends inclusive, at most 366 days counting both.
export const MAX_DAYS = 366;
export const CSV_PATH = "/admin/ksiega/csv";

export type PeriodProblem = "format" | "order" | "range";
export type Period = { from: string; to: string; problem: PeriodProblem | null };

const DAY_MS = 86_400_000;
const DAY_RE = /^(\d{4})-(\d{2})-(\d{2})$/;

/** The UTC day number of a real `yyyy-MM-dd` date, or null (also for 2026-02-30: it does not round-trip). */
export function dayNumber(day: string): number | null {
  const m = DAY_RE.exec(day);
  if (!m) return null;
  const t = Date.UTC(Number(m[1]), Number(m[2]) - 1, Number(m[3]));
  return new Date(t).toISOString().slice(0, 10) === day ? Math.round(t / DAY_MS) : null;
}

export const dayOf = (d: Date): string => d.toISOString().slice(0, 10);

export function problemOf(from: string, to: string): PeriodProblem | null {
  const a = dayNumber(from);
  const b = dayNumber(to);
  if (a === null || b === null) return "format";
  if (b < a) return "order";
  return b - a + 1 > MAX_DAYS ? "range" : null;
}

/** The period of the page: the query when both ends are given, otherwise the current month so far (UTC). */
export function periodOf(raw: { from?: unknown; to?: unknown }, now: Date): Period {
  if (raw.from === undefined && raw.to === undefined) return { ...currentMonth(now), problem: null };
  const from = typeof raw.from === "string" ? raw.from : ""; // a repeated parameter (array) is not a day
  const to = typeof raw.to === "string" ? raw.to : "";
  return { from, to, problem: problemOf(from, to) };
}

export function currentMonth(now: Date): { from: string; to: string } {
  const y = now.getUTCFullYear();
  const m = now.getUTCMonth();
  return { from: dayOf(new Date(Date.UTC(y, m, 1))), to: dayOf(now) };
}

export type PresetId = "thisMonth" | "lastMonth" | "thisYear";
export function presets(now: Date): { id: PresetId; from: string; to: string }[] {
  const y = now.getUTCFullYear();
  const m = now.getUTCMonth();
  return [
    { id: "thisMonth", ...currentMonth(now) },
    { id: "lastMonth", from: dayOf(new Date(Date.UTC(y, m - 1, 1))), to: dayOf(new Date(Date.UTC(y, m, 0))) },
    { id: "thisYear", from: dayOf(new Date(Date.UTC(y, 0, 1))), to: dayOf(now) },
  ];
}

export const csvHref = (from: string, to: string): string => `${CSV_PATH}?${new URLSearchParams({ from, to })}`;
export const csvFileName = (from: string, to: string): string => `ledger-${from}-${to}.csv`;
