// Pure slot logic: the API speaks UTC instants and dates, the page speaks Europe/Warsaw (CONTRACTS 4.2, SPEC 6.5).
export const ZONE = "Europe/Warsaw";
/** CONTRACTS 4.2: a request spans at most 14 days and lies within 60 days. The last offered date is today + 59 (the conservative reading of "within 60 days"). */
export const WINDOW_DAYS = 14;
export const HORIZON_DAYS = 60;
export type Slot = { startUtc: string; endUtc: string };
export type DayGroup = { date: string; slots: Slot[] };

const time = new Intl.DateTimeFormat("pl-PL", { timeZone: ZONE, hour: "2-digit", minute: "2-digit", hourCycle: "h23" });
const date = new Intl.DateTimeFormat("en-CA", { timeZone: ZONE, year: "numeric", month: "2-digit", day: "2-digit" });
const heading = new Intl.DateTimeFormat("pl-PL", { timeZone: "UTC", weekday: "long", day: "numeric", month: "long" });
const DAY_MS = 86_400_000;

/** `HH:mm` in Warsaw for a UTC instant. */
export const warsawTime = (utc: string): string => time.format(new Date(utc));
/** `YYYY-MM-DD` in Warsaw for a UTC instant. */
export const warsawDate = (utc: string | Date): string => date.format(typeof utc === "string" ? new Date(utc) : utc);
/** Polish heading for a `YYYY-MM-DD` local date, for instance `niedziela, 28 marca`. */
export const dayHeading = (ymd: string): string => heading.format(new Date(`${ymd}T12:00:00Z`));
const addDays = (ymd: string, n: number): string => new Date(Date.parse(`${ymd}T00:00:00Z`) + n * DAY_MS).toISOString().slice(0, 10);

/** The index-th request window as local dates (`from`, `to` inclusive), or null once the horizon is reached. */
export function slotWindow(now: Date, index: number): { from: string; to: string } | null {
  const today = warsawDate(now);
  const last = addDays(today, HORIZON_DAYS - 1);
  const from = addDays(today, index * WINDOW_DAYS);
  if (from > last) return null;
  const to = addDays(from, WINDOW_DAYS - 1);
  return { from, to: to > last ? last : to };
}

/** Defence in depth: whatever the API returns, offer only slots that have not started and lie inside the horizon. */
export function withinHorizon(slots: Slot[], now: Date): Slot[] {
  const last = addDays(warsawDate(now), HORIZON_DAYS - 1);
  return slots.filter((s) => Date.parse(s.startUtc) > now.getTime() && warsawDate(s.startUtc) <= last);
}

export function groupByWarsawDay(slots: Slot[]): DayGroup[] {
  const sorted = [...slots].sort((a, b) => Date.parse(a.startUtc) - Date.parse(b.startUtc));
  const groups: DayGroup[] = [];
  for (const s of sorted) {
    const d = warsawDate(s.startUtc);
    const g = groups.at(-1);
    if (g && g.date === d) g.slots.push(s);
    else groups.push({ date: d, slots: [s] });
  }
  return groups;
}
