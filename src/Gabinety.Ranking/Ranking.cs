namespace Gabinety.Ranking;

/// <summary>Constants and weights of CONTRACTS 9.3. They are the owner's: changing one changes <see cref="Version"/>, the changelog and the public page.</summary>
public static class RankingRules
{
    public const string Version = "2026-11-v1";
    public const int M = 5;                         // weight of the prior in the smoothed rating
    public const double Mu = 3.8;                   // prior mean rating
    public const double WeightRating = 0.45;
    public const double WeightReliability = 0.25;
    public const double WeightAvailability = 0.15;
    public const double WeightCompleteness = 0.15;
    public const int MinReviewsToShowRating = 3;
    public const int SlotsForFullAvailability = 10;
}

/// <param name="Reviews">n: published review count.</param>
/// <param name="Mean">r: mean of those reviews, 0 to 5 (0 when there are none).</param>
/// <param name="Completed">c: completed consultations.</param>
/// <param name="Cancellations">x: practitioner cancellations.</param>
/// <param name="NoShowsUpheld">s: upheld practitioner no-shows.</param>
/// <param name="Completeness">p: profile completeness in [0, 1].</param>
/// <param name="OpenSlots14d">k: open slots in the next 14 days.</param>
/// <remarks>No input is a payment, a plan, a promotion, a fee, a model output or a cost (CONTRACTS 9.3).</remarks>
public sealed record RankingInput(int Reviews, double Mean, int Completed, int Cancellations, int NoShowsUpheld, double Completeness, int OpenSlots14d);

public sealed record RankedItem(Guid Id, DateTimeOffset ApprovedAt, double Score);

public static class Ranker
{
    /// <summary>score = 100 * (0.45 B + 0.25 C + 0.15 A + 0.15 P), two decimals, half away from zero.</summary>
    public static double Score(RankingInput i)
    {
        Validate(i);
        double n = i.Reviews;
        var b = (n * i.Mean + RankingRules.M * RankingRules.Mu) / (n + RankingRules.M) / 5.0;
        var c = (i.Completed + 3.0) / ((double)i.Completed + i.Cancellations + i.NoShowsUpheld + 4.0);
        var a = Math.Min(1.0, i.OpenSlots14d / (double)RankingRules.SlotsForFullAvailability);
        var score = 100.0 * (RankingRules.WeightRating * b + RankingRules.WeightReliability * c
            + RankingRules.WeightAvailability * a + RankingRules.WeightCompleteness * i.Completeness);
        return Math.Round(score, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>Order: score descending, then approved-at ascending, then id ascending.</summary>
    public static int Compare(RankedItem x, RankedItem y)
    {
        var byScore = y.Score.CompareTo(x.Score);
        if (byScore != 0) return byScore;
        var byApproval = x.ApprovedAt.CompareTo(y.ApprovedAt);
        return byApproval != 0 ? byApproval : x.Id.CompareTo(y.Id);
    }

    private static void Validate(RankingInput i)
    {
        if (i.Reviews < 0) throw new ArgumentOutOfRangeException(nameof(i), "Reviews must not be negative.");
        if (i.Mean is < 0 or > 5 || double.IsNaN(i.Mean)) throw new ArgumentOutOfRangeException(nameof(i), "Mean must be between 0 and 5.");
        if (i.Completed < 0 || i.Cancellations < 0 || i.NoShowsUpheld < 0 || i.OpenSlots14d < 0) throw new ArgumentOutOfRangeException(nameof(i), "Counts must not be negative.");
        if (i.Completeness is < 0 or > 1 || double.IsNaN(i.Completeness)) throw new ArgumentOutOfRangeException(nameof(i), "Completeness must be between 0 and 1.");
    }
}

/// <summary>What a page shows. A tiny sample is never presented as an average: from <see cref="RankingRules.MinReviewsToShowRating"/> reviews, else "new".</summary>
public sealed record RatingDisplay(bool IsNew, double? Average, int Count);

public static class Display
{
    public static RatingDisplay Rating(int count, double mean) =>
        count < RankingRules.MinReviewsToShowRating
            ? new RatingDisplay(true, null, count)
            : new RatingDisplay(false, Math.Round(mean, 1, MidpointRounding.AwayFromZero), count);
}
