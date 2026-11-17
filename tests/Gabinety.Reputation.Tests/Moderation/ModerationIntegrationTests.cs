using System.Net;
using Gabinety.Contracts.Reputation;
using Gabinety.Reputation.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Gabinety.Reputation.Tests.Moderation;

/// <summary>PostgreSQL proof (CI `integration` job): the conditional UPDATE decides a race between administrators, and the summary equals a recount. Needs Docker.</summary>
[Trait("tier", "integration")]
public class ModerationIntegrationTests
{
    static async Task<ModerationHost> StartAsync(string cs)
    {
        var h = new ModerationHost();
        h.Settings["Database:Provider"] = "PostgreSQL";
        h.Settings["ConnectionStrings:reputationdb"] = cs;
        h.CreateClient();
        var state = h.Services.GetRequiredService<MigrationState>();
        await state.WaitForCompletionAsync(TimeSpan.FromSeconds(60));
        Assert.Equal(MigrationStatus.Done, state.Status);
        return h;
    }

    [Fact]
    public async Task Parallel_publishes_of_one_review_have_one_winner_and_the_summary_moves_once()
    {
        await using var pg = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await pg.StartAsync();
        using var h = await StartAsync(pg.GetConnectionString());
        var p = Guid.NewGuid(); var held = ModerationHost.NewReview(p, ReviewStatus.Held, 4);
        await h.Seed(p, held);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => ModerationHost.Publish(h.As($"demo-admin-{i}", "Admin"), held.Id)));

        // The same action cannot win twice: Held -> Published happens once, the other seven find it Published.
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.NoContent));
        Assert.Equal(7, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(ReviewStatus.Published, (await h.ReviewOf(held.Id)).Status);
        var s = await h.SummaryOf(p);
        var (count, sum) = await h.Recount(p);
        Assert.Equal((count, sum), (s?.Count ?? 0, s?.Sum ?? 0));
        Assert.Equal(1L, s?.Version ?? 0);
        Assert.Equal(2, (await h.EventsOf(held.Id)).Count); // Created plus the single decision
    }

    /// <summary>
    /// Publish and reject are DIFFERENT decisions, and CONTRACTS 8.3 lets an administrator reject a Published review, so a reject that is read after a publish
    /// has committed is a second legitimate decision (Held -> Published -> Rejected): one or two succeed depending on the interleaving (or a reject that read Held loses to a publish), never a 500, and
    /// whatever the order the stored summary is the recount and every success left exactly one event.
    /// </summary>
    [Fact]
    public async Task Parallel_publish_and_reject_decisions_end_in_a_consistent_state_whatever_the_interleaving()
    {
        await using var pg = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await pg.StartAsync();
        using var h = await StartAsync(pg.GetConnectionString());
        for (var round = 0; round < 5; round++)
        {
            var p = Guid.NewGuid(); var held = ModerationHost.NewReview(p, ReviewStatus.Held, 4);
            await h.Seed(p, held);

            var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(i =>
                i % 2 == 0 ? ModerationHost.Publish(h.As($"demo-admin-{i}", "Admin"), held.Id) : ModerationHost.Reject(h.As($"demo-admin-{i}", "Admin"), held.Id)));

            var codes = results.Select(r => r.StatusCode).ToArray();
            Assert.All(codes, c => Assert.True(c is HttpStatusCode.NoContent or HttpStatusCode.Conflict, $"round {round}: {string.Join(",", codes)}"));
            var wins = codes.Count(c => c == HttpStatusCode.NoContent);
            var review = await h.ReviewOf(held.Id);
            // Three legal interleavings: reject first (Held -> Rejected, one win); publish then a reject that read Published (two wins);
            // publish first while every reject had read Held, so each reject's conditional UPDATE matched nothing and lost (Published, one win).
            if (review.Status == ReviewStatus.Rejected) Assert.InRange(wins, 1, 2);
            else { Assert.Equal(ReviewStatus.Published, review.Status); Assert.Equal(1, wins); }
            var s = await h.SummaryOf(p);
            var (count, sum) = await h.Recount(p);
            Assert.Equal((count, sum), (s?.Count ?? 0, s?.Sum ?? 0)); // whatever the order, the stored summary is the recount
            Assert.Equal(1 + wins, (await h.EventsOf(held.Id)).Count); // Created plus one event per successful decision
        }
    }

    [Fact]
    public async Task Rejecting_a_published_review_removes_it_from_the_stored_summary()
    {
        await using var pg = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await pg.StartAsync();
        using var h = await StartAsync(pg.GetConnectionString());
        var p = Guid.NewGuid(); var a = ModerationHost.NewReview(p, ReviewStatus.Published, 5); var b = ModerationHost.NewReview(p, ReviewStatus.Published, 2);
        await h.Seed(p, a, b);

        Assert.Equal(HttpStatusCode.NoContent, (await ModerationHost.Reject(h.As("demo-admin", "Admin"), b.Id)).StatusCode);

        var s = (await h.SummaryOf(p))!;
        Assert.Equal((1, 5, 3L), (s.Count, s.Sum, s.Version));
        Assert.Equal(ReviewStatus.Rejected, (await h.ReviewOf(b.Id)).Status);
    }
}
