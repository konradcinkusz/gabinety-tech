using Gabinety.Ranking;

namespace Gabinety.Ranking.Tests;

public class RankerTests
{
    // CONTRACTS 9.3 test vectors, computed independently of the implementation; tolerance 0.005.
    [Theory]
    [InlineData(0, 0.0, 0, 0, 0, 1.0, 10, 82.95)]
    [InlineData(10, 4.8, 20, 1, 0, 0.8, 5, 82.70)]
    [InlineData(3, 5.0, 3, 0, 0, 0.5, 0, 67.18)]
    public void Contract_vectors(int n, double r, int c, int x, int s, double p, int k, double expected)
    {
        var score = Ranker.Score(new RankingInput(n, r, c, x, s, p, k));
        Assert.InRange(score, expected - 0.005, expected + 0.005);
    }

    [Fact]
    public void Constants_are_the_owners()
    {
        Assert.Equal("2026-11-v1", RankingRules.Version);
        Assert.Equal(5, RankingRules.M);
        Assert.Equal(3.8, RankingRules.Mu);
        Assert.Equal(new[] { 0.45, 0.25, 0.15, 0.15 }, new[] { RankingRules.WeightRating, RankingRules.WeightReliability, RankingRules.WeightAvailability, RankingRules.WeightCompleteness });
        Assert.Equal(3, RankingRules.MinReviewsToShowRating);
        Assert.Equal(10, RankingRules.SlotsForFullAvailability);
    }

    static RankingInput Base => new(10, 4.0, 10, 1, 0, 0.5, 5);
    static double S(RankingInput i) => Ranker.Score(i);

    [Fact]
    public void Monotonic_in_each_input()
    {
        for (var r = 0.0; r < 5.0; r += 0.25) Assert.True(S(Base with { Mean = r + 0.25 }) >= S(Base with { Mean = r }), $"rating {r}");
        for (var c = 0; c < 60; c++) Assert.True(S(Base with { Completed = c + 1 }) >= S(Base with { Completed = c }), $"completed {c}");
        for (var x = 0; x < 60; x++) Assert.True(S(Base with { Cancellations = x + 1 }) <= S(Base with { Cancellations = x }), $"cancellations {x}");
        for (var s = 0; s < 60; s++) Assert.True(S(Base with { NoShowsUpheld = s + 1 }) <= S(Base with { NoShowsUpheld = s }), $"no-shows {s}");
        for (var p = 0.0; p < 1.0; p += 0.1) Assert.True(S(Base with { Completeness = Math.Min(1, p + 0.1) }) >= S(Base with { Completeness = p }), $"completeness {p}");
        for (var k = 0; k < 30; k++) Assert.True(S(Base with { OpenSlots14d = k + 1 }) >= S(Base with { OpenSlots14d = k }), $"slots {k}");
    }

    [Fact]
    public void More_reviews_help_above_the_prior_mean_and_hurt_below_it()
    {
        for (var n = 0; n < 50; n++)
        {
            Assert.True(S(Base with { Reviews = n + 1, Mean = 4.5 }) >= S(Base with { Reviews = n, Mean = 4.5 }), $"above prior, n={n}");
            Assert.True(S(Base with { Reviews = n + 1, Mean = 2.0 }) <= S(Base with { Reviews = n, Mean = 2.0 }), $"below prior, n={n}");
        }
    }

    [Fact]
    public void Score_stays_between_0_and_100_over_the_whole_input_range()
    {
        foreach (var n in new[] { 0, 1, 3, 50, 100_000 })
        foreach (var r in new[] { 0.0, 1.0, 3.8, 5.0 })
        foreach (var c in new[] { 0, 5, 1_000_000 })
        foreach (var x in new[] { 0, 5, 1_000_000 })
        foreach (var p in new[] { 0.0, 0.5, 1.0 })
        foreach (var k in new[] { 0, 3, 10, 1000 })
        {
            var score = S(new RankingInput(n, n == 0 ? 0 : r, c, x, 0, p, k));
            Assert.InRange(score, 0.0, 100.0);
        }
    }

    [Fact]
    public void Out_of_range_input_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => S(Base with { Mean = 5.1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => S(Base with { Completeness = 1.1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => S(Base with { Reviews = -1 }));
    }

    [Fact]
    public void Ties_are_ordered_by_approved_at_then_id()
    {
        var t0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var a = new RankedItem(Guid.Parse("00000000-0000-0000-0000-000000000002"), t0, 80.0);
        var b = new RankedItem(Guid.Parse("00000000-0000-0000-0000-000000000001"), t0.AddDays(1), 80.0);
        var c = new RankedItem(Guid.Parse("00000000-0000-0000-0000-000000000003"), t0, 80.0);
        var higher = new RankedItem(Guid.Parse("00000000-0000-0000-0000-000000000009"), t0.AddDays(9), 80.01);
        var ordered = new[] { b, c, a, higher }.Order(Comparer<RankedItem>.Create(Ranker.Compare)).Select(i => i.Id.ToString()[^1]).ToArray();
        Assert.Equal(new[] { '9', '2', '3', '1' }, ordered); // score desc; ties: earlier approval first, then id
    }

    [Theory]
    [InlineData(0, 0.0, true, null)]
    [InlineData(2, 5.0, true, null)]
    [InlineData(3, 4.666, false, 4.7)]
    [InlineData(10, 3.04, false, 3.0)]
    [InlineData(3, 4.65, false, 4.7)] // one decimal, half away from zero
    public void Display_rating_only_from_three_reviews(int count, double mean, bool isNew, double? shown)
    {
        var d = Display.Rating(count, mean);
        Assert.Equal(isNew, d.IsNew);
        Assert.Equal(shown, d.Average);
        Assert.Equal(count, d.Count);
    }
}
