export type PriceResult = { ok: true; grosz: number } | { ok: false; reason: "format" | "range" };
/** CONTRACTS 3.1: price_minor 5000 to 150000 (Directory:Price:* may move these; the server stays the authority). */
export const MIN_GROSZ = 5000;
export const MAX_GROSZ = 150000;

/** "250" or "250,50" (a dot works too) to integer grosz. Never goes through a float, so 250,55 cannot become 25054. */
export function parsePricePln(input: string): PriceResult {
  const m = /^(\d{1,7})(?:[.,](\d{1,2}))?$/.exec(input.trim());
  if (!m) return { ok: false, reason: "format" };
  const grosz = Number(m[1]) * 100 + Number((m[2] ?? "").padEnd(2, "0"));
  return grosz < MIN_GROSZ || grosz > MAX_GROSZ ? { ok: false, reason: "range" } : { ok: true, grosz };
}

export function groszToInput(grosz: number): string {
  const frac = grosz % 100;
  return Math.floor(grosz / 100) + (frac === 0 ? "" : "," + String(frac).padStart(2, "0"));
}
