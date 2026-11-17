// Hand-declared shape of GET /api/v1/ranking-rules (CONTRACTS 3.2); the generated types predate it (see publicApi.ts).
export type RankingRules = {
  version: string;
  effectiveFrom: string;
  weights: { rating: number; reliability: number; availability: number; completeness: number };
  constants: { m: number; mu: number; minReviewsToShowRating: number; slotsForFullAvailability: number };
};
export type RankingInput = { n: number; r: number; c: number; x: number; s: number; p: number; k: number };
export type PartKey = keyof RankingRules["weights"];
export type Part = { key: PartKey; value: number; weight: number; points: number };

/** The inputs of the worked example. They are the second published vector of CONTRACTS 9.3. */
export const EXAMPLE_INPUT: RankingInput = { n: 10, r: 4.8, c: 20, x: 1, s: 0, p: 0.8, k: 5 };

const PART_ORDER: PartKey[] = ["rating", "reliability", "availability", "completeness"];

/** Round half away from zero to two decimals (CONTRACTS 9.3); inputs here are non-negative. */
const round2 = (v: number) => Math.round((v + Number.EPSILON) * 100) / 100;

/** Each part is a value in [0, 1] (CONTRACTS 9.3); the score is 100 * sum(weight * value), using the weights and constants the API returned. */
export function workedExample(rules: RankingRules, input: RankingInput = EXAMPLE_INPUT): { parts: Part[]; score: number } {
  const { m, mu, slotsForFullAvailability } = rules.constants;
  const values: Record<PartKey, number> = {
    rating: (input.n * input.r + m * mu) / (input.n + m) / 5,
    reliability: (input.c + 3) / (input.c + input.x + input.s + 4),
    availability: Math.min(1, input.k / slotsForFullAvailability),
    completeness: input.p,
  };
  const parts = PART_ORDER.map((key) => ({ key, value: values[key], weight: rules.weights[key], points: 100 * rules.weights[key] * values[key] }));
  return { parts, score: round2(parts.reduce((sum, p) => sum + p.points, 0)) };
}

export const rankingScore = (input: RankingInput, rules: RankingRules) => workedExample(rules, input).score;

/** Decimal comma, at most `max` decimals, no trailing zeros: 0.45 * 100 gives "45", 12.5 gives "12,5". */
export function formatNumber(v: number, max = 2): string {
  return String(Number(v.toFixed(max))).replace(".", ",");
}
export const formatFixed = (v: number, digits = 2) => v.toFixed(digits).replace(".", ",");
export const formatPercent = (weight: number) => `${formatNumber(weight * 100, 1)}%`;

/** "2026-11-13" or a full ISO timestamp to "13.11.2026" (no time zone arithmetic: the date part is the effective date). */
export function formatDate(iso: string): string {
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(iso);
  return m ? `${m[3]}.${m[2]}.${m[1]}` : iso;
}
