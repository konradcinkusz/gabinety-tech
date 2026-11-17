using System.Net;
using Gabinety.Contracts.Reputation;
using Gabinety.Reputation.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Gabinety.Reputation.Tests;

public class DemoResetApiTests
{
    static Task<HttpResponseMessage> Reset(InternalHost h, string? key = InternalHost.Key) => h.PostRaw("/internal/v1/demo/reset", "{}", key);

    [Fact]
    public async Task Reset_deletes_the_reviews_and_events_of_demo_reviewers_and_leaves_every_other_row_untouched_and_reports_counts()
    {
        await using var h = InternalHost.Fake();
        Guid onlyDemo = Guid.NewGuid(), mixed = Guid.NewGuid(), untouched = Guid.NewGuid();
        Review d1 = InternalHost.NewReview(onlyDemo, "demo-r1"), d2 = InternalHost.NewReview(mixed, "demo-r2", 1), r3 = InternalHost.NewReview(mixed, "real-r3", 4),
               r4 = InternalHost.NewReview(untouched, "real-r4", 3), erased = InternalHost.NewReview(onlyDemo, "demo-erased-0f0f", 2);
        await h.Seed(db =>
        {
            db.Reviews.AddRange(d1, d2, r3, r4, erased);
            db.RatingSummaries.AddRange(
                new RatingSummary { PractitionerId = onlyDemo, Count = 2, Sum = 7, Version = 4, PushedVersion = 4 },
                new RatingSummary { PractitionerId = mixed, Count = 2, Sum = 5, Version = 6, PushedVersion = 6 },
                new RatingSummary { PractitionerId = untouched, Count = 1, Sum = 3, Version = 9, PushedVersion = 8 });
            db.ModerationEvents.AddRange(InternalHost.NewEvent(d1.Id, ModerationEventKind.Created), InternalHost.NewEvent(d2.Id, ModerationEventKind.Created),
                InternalHost.NewEvent(d2.Id, ModerationEventKind.Published), InternalHost.NewEvent(r3.Id, ModerationEventKind.Created), InternalHost.NewEvent(r4.Id, ModerationEventKind.Created),
                InternalHost.NewEvent(erased.Id, ModerationEventKind.Created));
        });

        var r = await Reset(h);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var j = await r.Body();
        Assert.Equal(3, j.GetProperty("reviews").GetInt32());
        Assert.Equal(4, j.GetProperty("moderationEvents").GetInt32());
        Assert.Equal(1, j.GetProperty("ratingSummariesDeleted").GetInt32());
        Assert.Equal(1, j.GetProperty("ratingSummariesRecomputed").GetInt32());

        Assert.Equal(new[] { r3.Id, r4.Id }.Order(), (await h.Query(db => db.Reviews.Select(x => x.Id).ToListAsync())).Order());
        Assert.Equal(2, await h.Query(db => db.Reviews.CountAsync()));
        Assert.Equal(2, await h.Query(db => db.ModerationEvents.CountAsync()));
        Assert.Null(await h.SummaryOf(onlyDemo)); // nothing of that practitioner is left
        var m = (await h.SummaryOf(mixed))!; // recounted from the rows that remain, and marked to be pushed again
        Assert.Equal((1, 4), (m.Count, m.Sum));
        Assert.True(m.Version > m.PushedVersion);
        var u = (await h.SummaryOf(untouched))!; // not a demo practitioner's data: exactly as before
        Assert.Equal((1, 3, 9L, 8L), (u.Count, u.Sum, u.Version, u.PushedVersion));
    }

    [Fact]
    public async Task Prefix_match_is_case_sensitive_and_a_second_reset_changes_nothing()
    {
        await using var h = InternalHost.Fake();
        var p = Guid.NewGuid();
        Review upper = InternalHost.NewReview(p, "DEMO-upper"), within = InternalHost.NewReview(p, "not-demo-within"), demo = InternalHost.NewReview(p, "demo-x");
        await h.Seed(db => db.Reviews.AddRange(upper, within, demo));

        Assert.Equal(1, (await (await Reset(h)).Body()).GetProperty("reviews").GetInt32());
        var second = await (await Reset(h)).Body();
        Assert.Equal(0, second.GetProperty("reviews").GetInt32());
        Assert.Equal(0, second.GetProperty("moderationEvents").GetInt32());
        Assert.Equal(2, await h.Query(db => db.Reviews.CountAsync()));
    }

    [Fact]
    public async Task A_reset_with_nothing_to_do_is_200_with_zero_counts_and_a_wrong_key_deletes_nothing()
    {
        await using var h = InternalHost.Fake();
        await h.Seed(db => db.Reviews.Add(InternalHost.NewReview(Guid.NewGuid(), "demo-keep")));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Reset(h, "nope")).StatusCode);
        Assert.Equal(1, await h.Query(db => db.Reviews.CountAsync()));
    }
}
