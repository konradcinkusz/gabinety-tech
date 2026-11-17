using System.Net;
using Gabinety.Reputation.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Gabinety.Reputation.Tests.Reviews;

/// <summary>PostgreSQL proof (CI `integration` job): the unique index on booking_id and the atomic summary upsert. Needs Docker.</summary>
[Trait("tier", "integration")]
public class ReviewsIntegrationTests
{
    static async Task<ReviewsHost> StartAsync(string cs)
    {
        var h = new ReviewsHost();
        h.Settings["Database:Provider"] = "PostgreSQL";
        h.Settings["ConnectionStrings:reputationdb"] = cs;
        h.CreateClient();
        var state = h.Services.GetRequiredService<MigrationState>();
        await state.WaitForCompletionAsync(TimeSpan.FromSeconds(60));
        Assert.Equal(MigrationStatus.Done, state.Status);
        return h;
    }

    [Fact]
    public async Task The_unique_index_refuses_a_second_review_of_one_booking_even_when_the_application_check_is_bypassed()
    {
        await using var pg = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await pg.StartAsync();
        using var h = await StartAsync(pg.GetConnectionString());
        string user = "demo-user-1"; var pract = Guid.NewGuid(); var booking = h.Booking.Add(user, pract);
        var c = h.ClientFor(user);

        Assert.Equal(HttpStatusCode.Created, (await ReviewsHost.Post(c, booking)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await ReviewsHost.Post(c, booking)).StatusCode);

        // the same insert straight into the table: only the index can stop it
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => h.Seed(db => db.Reviews.Add(new Review
        {
            Id = Guid.NewGuid(), BookingId = booking, PractitionerId = pract, ClientUserId = user, Rating = 5, Text = ReviewsHost.GoodText,
            Status = Gabinety.Contracts.Reputation.ReviewStatus.Published, CreatedAt = h.Clock.GetUtcNow(),
        })));
        Assert.Contains("ux_reviews_booking_id", ex.InnerException?.Message ?? "");
        Assert.Equal(1, (await h.SummaryOf(pract))!.Count);
    }

    [Fact]
    public async Task Parallel_reviews_of_one_practitioner_lose_no_summary_update()
    {
        await using var pg = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await pg.StartAsync();
        using var h = await StartAsync(pg.GetConnectionString());
        var pract = Guid.NewGuid();
        var posts = Enumerable.Range(0, 12).Select(i =>
        {
            var user = $"demo-user-{i}";
            return ReviewsHost.Post(h.ClientFor(user), h.Booking.Add(user, pract), 1 + i % 5);
        }).ToList();

        Assert.All(await Task.WhenAll(posts), r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));

        var reviews = await h.ReviewsOf(pract);
        var s = (await h.SummaryOf(pract))!;
        Assert.Equal((reviews.Count, reviews.Sum(x => (int)x.Rating), (long)reviews.Count), (s.Count, s.Sum, s.Version));
    }
}
