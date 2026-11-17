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
    public async Task Parallel_decisions_on_one_review_have_one_winner_and_the_summary_moves_once()
    {
        await using var pg = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await pg.StartAsync();
        using var h = await StartAsync(pg.GetConnectionString());
        var p = Guid.NewGuid(); var held = ModerationHost.NewReview(p, ReviewStatus.Held, 4);
        await h.Seed(p, held);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(i =>
            i % 2 == 0 ? ModerationHost.Publish(h.As($"demo-admin-{i}", "Admin"), held.Id) : ModerationHost.Reject(h.As($"demo-admin-{i}", "Admin"), held.Id)));

        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.NoContent));
        Assert.Equal(7, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        var review = await h.ReviewOf(held.Id);
        var s = await h.SummaryOf(p);
        var (count, sum) = await h.Recount(p);
        Assert.Equal((count, sum), (s?.Count ?? 0, s?.Sum ?? 0)); // whichever side won, the summary is the recount
        Assert.Equal(review.Status == ReviewStatus.Published ? 1L : 0L, s?.Version ?? 0);
        Assert.Equal(2, (await h.EventsOf(held.Id)).Count); // Created plus the single decision
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
