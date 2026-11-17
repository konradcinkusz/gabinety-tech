import { internalKey, urls } from "./env";
import { fetchRetrying, json } from "./http";

/**
 * The test clock of Booking and Reputation (CONTRACTS 10.4): a signed offset added to each service's TimeProvider. Business time (the 24 h, 48 h and
 * 14 day windows) is passed by moving it, never by sleeping. The two services are moved together so that they agree on "now".
 */
const services = [urls.booking, urls.reputation] as const;
const headers = { "X-Internal-Key": internalKey, "Content-Type": "application/json" };

export interface ClockState { offsetMinutes: number; now: string }

export async function readClock(service: (typeof services)[number] = urls.booking): Promise<ClockState> {
  return json<ClockState>(await fetchRetrying(`${service}/internal/v1/test/clock`, { headers }), "read clock");
}

async function setAll(offsetMinutes: number): Promise<void> {
  for (const s of services) {
    await json(await fetchRetrying(`${s}/internal/v1/test/clock`, { method: "POST", headers, body: JSON.stringify({ offsetMinutes }) }), "set clock");
  }
}

/** Moves the clock `minutes` forward from where it is now (relative). Returns the new offset. */
export async function advanceClock(minutes: number): Promise<number> {
  const { offsetMinutes } = await readClock();
  const next = offsetMinutes + minutes;
  await setAll(next);
  return next;
}
export const advanceHours = (h: number) => advanceClock(Math.round(h * 60));
export const advanceDays = (d: number) => advanceClock(Math.round(d * 24 * 60));

/** Back to the real time (`offsetMinutes: 0`). Every journey that moved the clock restores it in `afterEach` (the `clock` fixture does it for you). */
export async function restoreClock(): Promise<void> {
  await setAll(0);
}
