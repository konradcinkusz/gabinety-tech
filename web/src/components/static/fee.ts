import { formatNumber } from "./ranking";

/** D-23: 15 % unless Billing:FirstConsultationFeeBps says otherwise. The same key the Billing service reads (CONTRACTS 9.1). */
export const DEFAULT_FEE_BPS = 1500;
const KEY = "Billing__FirstConsultationFeeBps";

/** Reads the rate per request, never at build time; anything that is not an integer in 0..10000 falls back to the default. */
export function feeBpsFrom(env: Record<string, string | undefined> = process.env): number {
  const raw = env[KEY]?.trim();
  if (!raw || !/^\d+$/.test(raw)) return DEFAULT_FEE_BPS;
  const bps = Number(raw);
  return bps <= 10_000 ? bps : DEFAULT_FEE_BPS;
}

export const formatBps = (bps: number) => `${formatNumber(bps / 100, 2)}%`;

/** CONTRACTS 9.1: (price * bps + 5000) / 10000 with integer division, which rounds half up. */
export const feeFor = (priceMinor: number, bps: number) => Math.floor((priceMinor * bps + 5000) / 10000);
