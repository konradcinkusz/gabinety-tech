using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gabinety.Contracts.Common;
using Gabinety.Contracts.Reputation;
using Gabinety.Reputation.Persistence;

namespace Gabinety.Reputation.Tests.Reviews;

public class ReviewsListTests
{
    static readonly DateTimeOffset T0 = new(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);

    static Review Row(Guid pract, ReviewStatus status, int minutes, string? reply = null) => new()
    {
        Id = Guid.NewGuid(), BookingId = Guid.NewGuid(), PractitionerId = pract, ClientUserId = "demo-secret-reviewer-id", Rating = 5,
        Text = $"Synthetyczna opinia numer {minutes} o długości powyżej dwudziestu znaków.", Status = status,
        CreatedAt = T0.AddMinutes(minutes), PublishedAt = status == ReviewStatus.Published ? T0.AddMinutes(minutes) : null,
        ReplyText = reply, RepliedAt = reply is null ? null : T0.AddMinutes(minutes + 1),
    };

    static async Task<Page<ReviewDto>> Get(HttpClient c, string url)
    {
        var r = await c.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<Page<ReviewDto>>(new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;
    }

    [Fact]
    public async Task The_public_list_never_includes_Held_or_Rejected_and_needs_no_token_and_hides_the_reviewer()
    {
        using var h = new ReviewsHost();
        var pract = Guid.NewGuid();
        var pub = Row(pract, ReviewStatus.Published, 1, reply: "Dziękuję za opinię!");
        await h.Seed(db => db.Reviews.AddRange(pub, Row(pract, ReviewStatus.Held, 2), Row(pract, ReviewStatus.Rejected, 3), Row(Guid.NewGuid(), ReviewStatus.Published, 4)));

        var raw = await (await h.ClientFor(null).GetAsync($"/api/v1/practitioners/{pract}/reviews")).Content.ReadAsStringAsync();
        var page = JsonSerializer.Deserialize<Page<ReviewDto>>(raw, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        var item = Assert.Single(page.Items);
        Assert.Equal(pub.Id, item.Id);
        Assert.Equal("Dziękuję za opinię!", item.Reply!.Text);
        Assert.Null(page.NextCursor);
        Assert.DoesNotContain("demo-secret-reviewer-id", raw);
        Assert.DoesNotContain("clientUserId", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bookingId", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("status", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Pages_are_newest_first_follow_the_cursor_and_cover_every_review_exactly_once()
    {
        using var h = new ReviewsHost();
        var pract = Guid.NewGuid();
        var rows = Enumerable.Range(1, 5).Select(i => Row(pract, ReviewStatus.Published, i)).ToList();
        rows.Add(Row(pract, ReviewStatus.Published, 5)); // same instant as the newest: the id breaks the tie
        await h.Seed(db => db.Reviews.AddRange(rows));
        var c = h.ClientFor(null);

        var seen = new List<Guid>();
        string? cursor = null; var pages = 0;
        do
        {
            var page = await Get(c, $"/api/v1/practitioners/{pract}/reviews?limit=2" + (cursor is null ? "" : $"&cursor={cursor}"));
            Assert.True(page.Items.Count <= 2);
            seen.AddRange(page.Items.Select(x => x.Id));
            cursor = page.NextCursor; pages++;
        } while (cursor is not null && pages < 10);

        Assert.Equal(3, pages);
        Assert.Equal(rows.Count, seen.Count);
        Assert.Equal(rows.Select(x => x.Id).Order(), seen.Order());
        var byId = rows.ToDictionary(x => x.Id);
        Assert.Equal(seen.OrderByDescending(id => byId[id].PublishedAt).ToList().Select(id => byId[id].PublishedAt), seen.Select(id => byId[id].PublishedAt));
    }

    [Theory]
    [InlineData("")]
    [InlineData("&limit=100")]
    [InlineData("&limit=0")]
    public async Task The_page_size_is_capped_at_20(string query)
    {
        using var h = new ReviewsHost();
        var pract = Guid.NewGuid();
        await h.Seed(db => db.Reviews.AddRange(Enumerable.Range(1, 25).Select(i => Row(pract, ReviewStatus.Published, i))));

        var page = await Get(h.ClientFor(null), $"/api/v1/practitioners/{pract}/reviews?x=1{query}");

        Assert.Equal(query == "&limit=0" ? 1 : 20, page.Items.Count);
        Assert.NotNull(page.NextCursor);
    }

    [Fact]
    public async Task A_broken_cursor_is_400_and_an_unknown_practitioner_is_an_empty_page()
    {
        using var h = new ReviewsHost();
        var c = h.ClientFor(null);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync($"/api/v1/practitioners/{Guid.NewGuid()}/reviews?cursor=not-a-cursor")).StatusCode);
        Assert.Empty((await Get(c, $"/api/v1/practitioners/{Guid.NewGuid()}/reviews")).Items);
    }

    [Fact]
    public async Task A_review_created_through_the_API_shows_in_the_list()
    {
        using var h = new ReviewsHost();
        string user = "demo-" + Guid.NewGuid().ToString("N")[..10]; var pract = Guid.NewGuid();
        await ReviewsHost.Post(h.ClientFor(user), h.Booking.Add(user, pract), 5);

        var page = await Get(h.ClientFor(null), $"/api/v1/practitioners/{pract}/reviews");

        var item = Assert.Single(page.Items);
        Assert.Equal(ReviewsHost.GoodText, item.Text);
        Assert.Equal(h.Clock.GetUtcNow(), item.CreatedAt);
    }
}
