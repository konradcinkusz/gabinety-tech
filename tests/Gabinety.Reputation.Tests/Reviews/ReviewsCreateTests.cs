using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gabinety.Contracts.Reputation;
using Gabinety.Reputation.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Gabinety.Reputation.Tests.Reviews;

public class ReviewsCreateTests
{
    static string Id() => "demo-" + Guid.NewGuid().ToString("N")[..12];

    static async Task<JsonElement> Body(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    [Fact]
    public async Task A_review_that_passes_the_rules_is_created_Published_and_the_summary_moves_in_the_same_step()
    {
        using var h = new ReviewsHost();
        string user = Id(); var pract = Guid.NewGuid(); var booking = h.Booking.Add(user, pract);

        var r = await ReviewsHost.Post(h.ClientFor(user), booking, rating: 4);

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var body = await Body(r);
        Assert.Equal("Published", body.GetProperty("status").GetString());
        var review = Assert.Single(await h.ReviewsOf(pract));
        Assert.Equal(body.GetProperty("reviewId").GetGuid(), review.Id);
        Assert.Equal((user, booking, ReviewStatus.Published, (short)4), (review.ClientUserId, review.BookingId, review.Status, review.Rating));
        Assert.Equal(h.Clock.GetUtcNow(), review.PublishedAt);
        var s = (await h.SummaryOf(pract))!;
        Assert.Equal((1, 4, 1L), (s.Count, s.Sum, s.Version));
        var kinds = await h.Query(db => db.ModerationEvents.Where(e => e.ReviewId == review.Id).OrderBy(e => e.Kind).Select(e => e.Kind).ToListAsync());
        Assert.Equal([ModerationEventKind.Created, ModerationEventKind.Published], kinds);
    }

    [Fact]
    public async Task A_review_that_trips_a_rule_is_Held_with_the_reason_and_does_not_touch_the_summary()
    {
        using var h = new ReviewsHost();
        string user = Id(); var pract = Guid.NewGuid(); var booking = h.Booking.Add(user, pract);

        var r = await ReviewsHost.Post(h.ClientFor(user), booking, text: "Zajrzyjcie na https://spam.demo.invalid/oferta po więcej szczegółów o spotkaniu.");

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.Equal("Held", (await Body(r)).GetProperty("status").GetString());
        var review = Assert.Single(await h.ReviewsOf(pract));
        Assert.Equal(ReviewStatus.Held, review.Status);
        Assert.Equal(["contains_link"], review.HoldReasons);
        Assert.Null(review.PublishedAt);
        Assert.Null(await h.SummaryOf(pract));
    }

    [Fact]
    public async Task Someone_elses_booking_is_404_and_nothing_is_written()
    {
        using var h = new ReviewsHost();
        string owner = Id(), intruder = Id(); var pract = Guid.NewGuid(); var booking = h.Booking.Add(owner, pract);

        var r = await ReviewsHost.Post(h.ClientFor(intruder), booking);

        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Empty(await h.ReviewsOf(pract));
        Assert.Null(await h.SummaryOf(pract));
    }

    [Fact]
    public async Task An_unknown_booking_is_404()
    {
        using var h = new ReviewsHost();
        var r = await ReviewsHost.Post(h.ClientFor(Id()), Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task A_booking_Booking_says_is_not_reviewable_is_422_not_reviewable_and_nothing_is_written()
    {
        using var h = new ReviewsHost();
        string user = Id(); var pract = Guid.NewGuid(); var booking = h.Booking.Add(user, pract, reviewable: false);

        var r = await ReviewsHost.Post(h.ClientFor(user), booking);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Equal("not_reviewable", (await Body(r)).GetProperty("code").GetString());
        Assert.Empty(await h.ReviewsOf(pract));
        Assert.Null(await h.SummaryOf(pract));
    }

    [Fact]
    public async Task Two_reviews_for_one_booking_give_409_already_reviewed_and_the_summary_counts_one()
    {
        using var h = new ReviewsHost();
        string user = Id(); var pract = Guid.NewGuid(); var booking = h.Booking.Add(user, pract);
        var c = h.ClientFor(user);

        Assert.Equal(HttpStatusCode.Created, (await ReviewsHost.Post(c, booking, 5)).StatusCode);
        var second = await ReviewsHost.Post(c, booking, 1);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("already_reviewed", (await Body(second)).GetProperty("code").GetString());
        Assert.Single(await h.ReviewsOf(pract));
        var s = (await h.SummaryOf(pract))!;
        Assert.Equal((1, 5, 1L), (s.Count, s.Sum, s.Version));
    }

    [Fact]
    public async Task A_repeat_after_the_window_closed_is_still_409_not_422()
    {
        using var h = new ReviewsHost();
        string user = Id(); var pract = Guid.NewGuid(); var booking = h.Booking.Add(user, pract);
        var c = h.ClientFor(user);
        await ReviewsHost.Post(c, booking);

        h.Booking.Bookings[booking] = (false, user, pract); // Booking now says the 14 days are over

        Assert.Equal(HttpStatusCode.Conflict, (await ReviewsHost.Post(c, booking)).StatusCode);
    }

    [Fact]
    public async Task Two_simultaneous_reviews_of_one_booking_give_exactly_one_201_and_one_409()
    {
        using var h = new ReviewsHost();
        string user = Id(); var pract = Guid.NewGuid(); var booking = h.Booking.Add(user, pract);
        var c = h.ClientFor(user);

        var codes = (await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => ReviewsHost.Post(c, booking)) /* 5: the per-user limit of CONTRACTS 10.2 is 5 an hour */)).Select(r => r.StatusCode).ToList();

        Assert.Equal(1, codes.Count(x => x == HttpStatusCode.Created));
        Assert.Equal(4, codes.Count(x => x == HttpStatusCode.Conflict));
        Assert.Equal(1, (await h.SummaryOf(pract))!.Count);
    }

    [Fact]
    public async Task The_summary_count_and_sum_equal_the_published_reviews_and_Held_ones_stay_out()
    {
        using var h = new ReviewsHost();
        var pract = Guid.NewGuid();
        var ratings = new[] { 5, 3, 4 };
        foreach (var rating in ratings)
        {
            string user = Id();
            Assert.Equal(HttpStatusCode.Created, (await ReviewsHost.Post(h.ClientFor(user), h.Booking.Add(user, pract), rating)).StatusCode);
        }
        string held = Id();
        await ReviewsHost.Post(h.ClientFor(held), h.Booking.Add(held, pract), 1, "Kontakt: demo.osoba@example.invalid, napiszcie do mnie w sprawie spotkania.");

        var reviews = await h.ReviewsOf(pract);
        var published = reviews.Where(x => x.Status == ReviewStatus.Published).ToList();
        var s = (await h.SummaryOf(pract))!;
        Assert.Equal(4, reviews.Count);
        Assert.Equal((published.Count, published.Sum(x => (int)x.Rating), 3L), (s.Count, s.Sum, s.Version));
        Assert.Equal((3, 12), (s.Count, s.Sum));
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(6, null)]
    [InlineData(5, "za krótko")]
    [InlineData(5, "")]
    public async Task Invalid_input_is_422_and_Booking_is_not_asked(int rating, string? shortText)
    {
        using var h = new ReviewsHost();
        string user = Id(); var booking = h.Booking.Add(user, Guid.NewGuid());

        var r = await ReviewsHost.Post(h.ClientFor(user), booking, rating, shortText ?? ReviewsHost.GoodText);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Equal("validation_failed", (await Body(r)).GetProperty("code").GetString());
        Assert.Equal(0, h.Booking.Calls);
    }

    [Fact]
    public async Task Without_a_token_the_answer_is_401()
    {
        using var h = new ReviewsHost();
        Assert.Equal(HttpStatusCode.Unauthorized, (await ReviewsHost.Post(h.ClientFor(null), Guid.NewGuid())).StatusCode);
    }

    [Fact]
    public async Task A_Booking_outage_is_503_dependency_unavailable_writes_nothing_and_leaks_neither_url_nor_key()
    {
        using var h = new ReviewsHost();
        string user = Id(); var pract = Guid.NewGuid(); var booking = h.Booking.Add(user, pract);
        h.Booking.Down = true;

        var r = await ReviewsHost.Post(h.ClientFor(user), booking);
        var text = await r.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode);
        Assert.Equal("dependency_unavailable", JsonDocument.Parse(text).RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain(FakeBooking.Key, text);
        Assert.DoesNotContain("booking.demo.invalid", text);
        Assert.Empty(await h.ReviewsOf(pract));
    }

    [Fact]
    public async Task The_review_text_never_appears_in_a_log_for_a_published_a_held_a_rejected_or_a_failed_request()
    {
        using var h = new ReviewsHost();
        const string marker = "ZNACZNIK-TEKSTU-9f3a1c";
        string user = Id(); var pract = Guid.NewGuid();
        var c = h.ClientFor(user);

        var ok = await ReviewsHost.Post(c, h.Booking.Add(user, pract), 5, $"{marker} spotkanie było bardzo pomocne i rzeczowe.");
        var heldUser = Id(); var cHeld = h.ClientFor(heldUser);
        var held = await ReviewsHost.Post(cHeld, h.Booking.Add(heldUser, pract), 5, $"{marker} zobacz https://spam.demo.invalid/x oraz https://spam2.demo.invalid/y");
        var dupBooking = h.Booking.Add(user, pract, reviewable: false);
        var notReviewable = await ReviewsHost.Post(c, dupBooking, 5, $"{marker} spotkanie było bardzo pomocne i rzeczowe.");
        h.Booking.Down = true;
        var down = await ReviewsHost.Post(c, h.Booking.Add(user, pract), 5, $"{marker} spotkanie było bardzo pomocne i rzeczowe.");

        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Created, HttpStatusCode.UnprocessableEntity, HttpStatusCode.ServiceUnavailable],
            [ok.StatusCode, held.StatusCode, notReviewable.StatusCode, down.StatusCode]);
        Assert.NotEmpty(h.Logs.Lines); // the sink works
        Assert.DoesNotContain(h.Logs.Lines, l => l.Contains(marker));
        foreach (var resp in new[] { ok, held, notReviewable, down }) Assert.DoesNotContain(marker, await resp.Content.ReadAsStringAsync());
    }
}
