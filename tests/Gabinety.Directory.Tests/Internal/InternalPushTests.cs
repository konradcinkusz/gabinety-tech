using System.Net;
using Gabinety.Contracts.Directory;
using Gabinety.Ranking;

namespace Gabinety.Directory.Tests.Internal;

public class InternalPushTests(InternalFactory f) : IClassFixture<InternalFactory>
{
    static Task<Gabinety.Directory.Features.Profiles.Practitioner> Approved(InternalFactory f) => f.Seed(PractitionerStatus.Approved, x =>
    {
        x.Headline = "demo"; x.RankingScore = 1m; x.RankingVersion = "old";
    });

    [Fact]
    public async Task Reputation_with_a_higher_version_updates_the_row_and_the_ranking_score()
    {
        var p = await Approved(f);
        var before = await f.Load(p.Id);
        var r = await Ik.PutReputation(f.Internal(), p.Id, 10, 4.8, 5);
        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        var after = await f.Load(p.Id);
        Assert.Equal(10, after.RatingCount);
        Assert.Equal(4.8m, after.RatingAvg);
        Assert.Equal(5, after.ReputationVersion);
        Assert.NotEqual(before.RankingScore, after.RankingScore);
        var expected = Math.Round((decimal)Ranker.Score(new RankingInput(10, 4.8, 0, 0, 0, 0, 0)), 2, MidpointRounding.AwayFromZero);
        Assert.Equal(expected, after.RankingScore);
        Assert.Equal(RankingRules.Version, after.RankingVersion);
    }

    [Theory]
    [InlineData(5)] // equal
    [InlineData(4)] // lower
    public async Task Reputation_with_an_equal_or_lower_version_is_204_and_changes_nothing(long version)
    {
        var p = await Approved(f);
        await Ik.PutReputation(f.Internal(), p.Id, 10, 4.8, 5);
        var before = await f.Load(p.Id);
        var r = await Ik.PutReputation(f.Internal(), p.Id, 99, 1.0, version);
        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        var after = await f.Load(p.Id);
        Assert.Equal(before.RatingCount, after.RatingCount);
        Assert.Equal(before.RatingAvg, after.RatingAvg);
        Assert.Equal(before.ReputationVersion, after.ReputationVersion);
        Assert.Equal(before.RankingScore, after.RankingScore);
    }

    [Fact]
    public async Task The_same_reputation_request_twice_equals_once()
    {
        var p = await Approved(f);
        await Ik.PutReputation(f.Internal(), p.Id, 3, 5.0, 2);
        var once = await f.Load(p.Id);
        Assert.Equal(HttpStatusCode.NoContent, (await Ik.PutReputation(f.Internal(), p.Id, 3, 5.0, 2)).StatusCode);
        var twice = await f.Load(p.Id);
        Assert.Equal(once.RankingScore, twice.RankingScore);
        Assert.Equal(once.UpdatedAt, twice.UpdatedAt);
        Assert.Equal(once.ReputationVersion, twice.ReputationVersion);
    }

    [Fact]
    public async Task Stats_with_a_higher_version_update_the_row_the_completeness_and_the_ranking_score()
    {
        var p = await Approved(f);
        var before = await f.Load(p.Id);
        var r = await Ik.PutStats(f.Internal(), p.Id, 20, 1, 0, 10, 7);
        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        var a = await f.Load(p.Id);
        Assert.Equal((20, 1, 0, 10, 7L), (a.CompletedCount, a.PractitionerCancelCount, a.NoShowUpheldCount, a.OpenSlots14d, a.StatsVersion));
        Assert.Equal(0.25m, a.ProfileCompleteness); // headline 0.15 + open slots 0.10 (CONTRACTS 3.5); the seeded row had no stored completeness
        var expected = Math.Round((decimal)Ranker.Score(new RankingInput(0, 0, 20, 1, 0, (double)a.ProfileCompleteness, 10)), 2, MidpointRounding.AwayFromZero);
        Assert.Equal(expected, a.RankingScore);
        Assert.NotEqual(before.RankingScore, a.RankingScore);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(3)]
    public async Task Stats_with_an_equal_or_lower_version_change_nothing(long version)
    {
        var p = await Approved(f);
        await Ik.PutStats(f.Internal(), p.Id, 20, 1, 0, 10, 7);
        var before = await f.Load(p.Id);
        Assert.Equal(HttpStatusCode.NoContent, (await Ik.PutStats(f.Internal(), p.Id, 0, 9, 9, 0, version)).StatusCode);
        var after = await f.Load(p.Id);
        Assert.Equal((before.CompletedCount, before.PractitionerCancelCount, before.NoShowUpheldCount, before.OpenSlots14d, before.StatsVersion, before.RankingScore, before.ProfileCompleteness),
            (after.CompletedCount, after.PractitionerCancelCount, after.NoShowUpheldCount, after.OpenSlots14d, after.StatsVersion, after.RankingScore, after.ProfileCompleteness));
    }

    [Fact]
    public async Task Reputation_and_stats_versions_are_independent()
    {
        var p = await Approved(f);
        await Ik.PutReputation(f.Internal(), p.Id, 3, 5.0, 10);
        Assert.Equal(HttpStatusCode.NoContent, (await Ik.PutStats(f.Internal(), p.Id, 4, 0, 0, 1, 1)).StatusCode);
        var a = await f.Load(p.Id);
        Assert.Equal(4, a.CompletedCount);
        Assert.Equal(10, a.ReputationVersion);
    }

    [Fact]
    public async Task A_practitioner_that_is_not_Approved_keeps_the_pushed_values_but_is_not_scored()
    {
        var p = await f.Seed(PractitionerStatus.Suspended, x => { x.RankingScore = 0; x.RankingVersion = ""; });
        Assert.Equal(HttpStatusCode.NoContent, (await Ik.PutReputation(f.Internal(), p.Id, 5, 4.0, 1)).StatusCode);
        var a = await f.Load(p.Id);
        Assert.Equal(5, a.RatingCount);
        Assert.Equal(0m, a.RankingScore);
        Assert.Equal("", a.RankingVersion);
    }

    [Fact]
    public async Task Unknown_practitioner_is_404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Ik.PutReputation(f.Internal(), Guid.NewGuid(), 1, 5.0, 1)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Ik.PutStats(f.Internal(), Guid.NewGuid(), 1, 0, 0, 0, 1)).StatusCode);
    }

    [Theory]
    [InlineData(-1, 4.0, 1)]
    [InlineData(1, 5.01, 1)]
    [InlineData(1, -0.1, 1)]
    [InlineData(1, 4.0, -1)]
    public async Task Reputation_with_out_of_range_values_is_400_and_changes_nothing(int count, double avg, long version)
    {
        var p = await Approved(f);
        var r = await Ik.PutReputation(f.Internal(), p.Id, count, avg, version);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("validation_failed", (await Verification.Vk.Problem(r)).Code);
        Assert.Equal(0, (await f.Load(p.Id)).ReputationVersion);
    }

    [Theory]
    [InlineData(-1, 0, 0, 0, 1)]
    [InlineData(0, -1, 0, 0, 1)]
    [InlineData(0, 0, -1, 0, 1)]
    [InlineData(0, 0, 0, -1, 1)]
    [InlineData(0, 0, 0, 0, -1)]
    public async Task Stats_with_negative_values_are_400(int c, int x, int s, int k, long version)
    {
        var p = await Approved(f);
        Assert.Equal(HttpStatusCode.BadRequest, (await Ik.PutStats(f.Internal(), p.Id, c, x, s, k, version)).StatusCode);
        Assert.Equal(0, (await f.Load(p.Id)).StatsVersion);
    }
}
