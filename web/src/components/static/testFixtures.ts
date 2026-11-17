// Synthetic data for tests. Not imported by production code.
import type { RankingInput, RankingRules } from "./ranking";

export const RANKING_EXAMPLE: RankingInput = { n: 10, r: 4.8, c: 20, x: 1, s: 0, p: 0.8, k: 5 };

export const rulesFixture = (over: Partial<RankingRules> = {}): RankingRules => ({
  version: "2026-11-v1",
  effectiveFrom: "2026-11-13T00:00:00Z",
  weights: { rating: 0.45, reliability: 0.25, availability: 0.15, completeness: 0.15 },
  constants: { m: 5, mu: 3.8, minReviewsToShowRating: 3, slotsForFullAvailability: 10 },
  ...over,
});
