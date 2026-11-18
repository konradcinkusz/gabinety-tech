# Ranking changelog

Every version of the public ranking rules (CONTRACTS 9.3, ADR-0008) with the date it took effect, the weights and constants, and why.
Changing a weight, a constant, an input or the version adds a **new last row** here, bumps `RankingRules.Version` in
`src/Gabinety.Ranking/Ranking.cs`, and updates the public page `/zasady-rankingu` in the same PR. Old rows are never edited.
The page and `GET /api/v1/ranking-rules` read their numbers from `RankingRules`, so they follow the code.

The test `tests/Gabinety.Ranking.Tests/ChangelogTests.cs` fails when the **Hash** of the last row differs from the hash of the current
`RankingRules` or when the last row's **Version** differs from `RankingRules.Version`. Its message gives the new hash to paste.

## Canonical form of the hash

`Hash` is the lowercase hex SHA-256 of the UTF-8 bytes (no BOM) of this text:

- one line per public `const` field of `RankingRules` (found by reflection, so a new constant also changes the hash);
- lines ordered by field name, ordinal (`M`, `MinReviewsToShowRating`, `Mu`, `SlotsForFullAvailability`, `Version`, `WeightAvailability`, `WeightCompleteness`, `WeightRating`, `WeightReliability`);
- each line is `Name=Value` followed by a single LF (also after the last line, no CR);
- values: strings verbatim; integers in invariant decimal; doubles in invariant round-trip form (`0.45`, `3.8`).

For `2026-11-v1` the text is exactly (9 lines):

```
M=5
MinReviewsToShowRating=3
Mu=3.8
SlotsForFullAvailability=10
Version=2026-11-v1
WeightAvailability=0.15
WeightCompleteness=0.15
WeightRating=0.45
WeightReliability=0.25
```

Reproduce by hand: `printf 'M=5\nMinReviewsToShowRating=3\nMu=3.8\nSlotsForFullAvailability=10\nVersion=2026-11-v1\nWeightAvailability=0.15\nWeightCompleteness=0.15\nWeightRating=0.45\nWeightReliability=0.25\n' | sha256sum`

## Versions

Keep this the only table in the file. The newest row is the last one.

| Date | Version | Hash | Constants | Why |
|---|---|---|---|---|
| 2026-11-16 | `2026-11-v1` | `55acb196c606465c50bf17bcb9ad278c7c07651c7d39015372057461eea95e43` | weights: rating 0.45, reliability 0.25, availability 0.15, completeness 0.15; m = 5, mu = 3.8, minReviewsToShowRating = 3, slotsForFullAvailability = 10 | First published version (CONTRACTS 9.3): a smoothed rating, a reliability share, availability over 14 days and profile completeness; no payment, plan or promotion is an input (ADR-0008). |
