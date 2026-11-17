using Gabinety.Contracts.Directory;
using Gabinety.Directory.Features.Profiles;
using Gabinety.Ranking;

namespace Gabinety.Directory.Features.Ranking;

/// <summary>
/// The one place that turns a practitioner row into `ranking_score` and `ranking_version` (CONTRACTS 9.3). Stateless, so a static call fits every
/// call site: approve, profile and offering edits, GitHub link, and the internal pushes of T-03.08. Only Approved rows are scored; the caller saves.
/// </summary>
public static class RankingRecalculator
{
    /// <returns>true when the row was scored, false for a row that is not Approved (left untouched).</returns>
    public static bool Recompute(Practitioner p)
    {
        if (p.Status != PractitionerStatus.Approved) return false;
        p.RankingScore = (decimal)Ranker.Score(new RankingInput(p.RatingCount, (double)p.RatingAvg, p.CompletedCount, p.PractitionerCancelCount,
            p.NoShowUpheldCount, (double)p.ProfileCompleteness, p.OpenSlots14d));
        p.RankingVersion = RankingRules.Version;
        return true;
    }
}
