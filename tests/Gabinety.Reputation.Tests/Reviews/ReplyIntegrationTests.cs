using System.Net;
using Gabinety.Contracts.Reputation;
using Gabinety.Reputation.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Gabinety.Reputation.Tests.Reviews;

/// <summary>PostgreSQL proof (CI `integration` job) that the reply is decided by the database under concurrency. Needs Docker.</summary>
[Trait("tier", "integration")]
public class ReplyIntegrationTests
{
    static async Task<ReplyHost> StartAsync(string cs)
    {
        var h = new ReplyHost();
        h.ExtraSettings["Database:Provider"] = "PostgreSQL";
        h.ExtraSettings["ConnectionStrings:reputationdb"] = cs;
        h.CreateClient();
        var state = h.Services.GetRequiredService<MigrationState>();
        await state.WaitForCompletionAsync(TimeSpan.FromSeconds(60));
        Assert.Equal(MigrationStatus.Done, state.Status);
        return h;
    }

    [Fact]
    public async Task Two_concurrent_replies_to_one_review_give_exactly_one_2xx_and_one_409_and_one_stored_reply()
    {
        await using var pg = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await pg.StartAsync();
        using var h = await StartAsync(pg.GetConnectionString());
        var user = "demo-pract-1"; var pract = h.Directory.Add(user);
        for (var round = 0; round < 10; round++)
        {
            var id = await h.SeedReview(pract);

            var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(i => ReplyHost.Reply(h.ClientFor(user), id, $"Odpowiedź numer {i} z synthetycznego testu.")));

            // every legal interleaving: exactly one winner, the loser is 409, never a 5xx
            Assert.Equal(1, results.Count(r => (int)r.StatusCode is >= 200 and < 300));
            Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
            Assert.Single(await h.EventsOf(id));
            Assert.NotNull((await h.ReviewOf(id)).ReplyText);
        }
    }

    [Fact]
    public async Task A_reply_racing_a_rejection_never_replies_to_a_Rejected_review_and_never_returns_5xx()
    {
        await using var pg = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await pg.StartAsync();
        using var h = await StartAsync(pg.GetConnectionString());
        var user = "demo-pract-2"; var pract = h.Directory.Add(user);
        for (var round = 0; round < 10; round++)
        {
            var id = await h.SeedReview(pract);
            var reply = ReplyHost.Reply(h.ClientFor(user), id);
            var reject = Task.Run(async () =>
            {
                using var scope = h.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ReputationDbContext>();
                return await Features.Moderation.ReviewModerator.ApplyAsync(db, id, Features.Moderation.ModerationAction.Reject, "demo-admin", "x", h.Clock.GetUtcNow(), default);
            });

            var r = await reply; await reject;

            // either order is legal: reply first (201, reply kept on a Rejected row, which is not public), or reject first (404)
            Assert.Contains(r.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.NotFound });
            var row = await h.ReviewOf(id);
            Assert.Equal(ReviewStatus.Rejected, row.Status);
            Assert.Equal(r.StatusCode == HttpStatusCode.Created, row.ReplyText is not null);
        }
    }
}
