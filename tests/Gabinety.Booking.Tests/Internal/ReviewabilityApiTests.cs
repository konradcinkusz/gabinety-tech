using System.Net;
using System.Text.Json;
using BookingEntity = Gabinety.Booking.Features.Bookings.Entities.Booking;
using BookingStatus = Gabinety.Billing.BookingStatus;

namespace Gabinety.Booking.Tests;

public class ReviewabilityApiTests
{
    const string InternalKey = "k-internal-DO-NOT-LEAK-5678";
    static readonly TimeSpan Window = TimeSpan.FromDays(14);

    static CreateHost NewHost()
    {
        var h = new CreateHost();
        h.Settings["Internal:Key"] = InternalKey;
        return h;
    }

    static async Task<HttpResponseMessage> Get(CreateHost h, Guid id, string? key = InternalKey)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, $"/internal/v1/bookings/{id}/reviewability");
        if (key != null) req.Headers.Add("X-Internal-Key", key);
        return await h.CreateClient().SendAsync(req);
    }

    static async Task<Guid> Seed(CreateHost h, BookingStatus status, string? resolution = null, DateTimeOffset? endedAt = null, DateTimeOffset? endUtc = null)
    {
        var id = Guid.NewGuid();
        var end = endUtc ?? h.Clock.GetUtcNow().AddDays(-1);
        await h.Seed(db => db.Bookings.Add(new BookingEntity
        {
            Id = id, ClientUserId = "demo-client-rev", PractitionerId = h.Practitioner, PractitionerUserId = "demo-practitioner-user",
            OfferingId = h.Offering, OfferingTitle = "demo-offering", DurationMinutes = 60, PriceMinor = 12345,
            StartUtc = end.AddHours(-1), EndUtc = end, Status = status, BriefText = "demo-brief-SECRET-TEXT",
            IdempotencyKey = Guid.NewGuid(), CreatedAt = end.AddDays(-2), EndedAt = endedAt, DisputeResolution = resolution,
        }));
        return id;
    }

    static async Task<JsonElement> Json(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    [Theory]
    [InlineData(BookingStatus.Completed, null, true)]
    [InlineData(BookingStatus.Resolved, "release", true)]
    [InlineData(BookingStatus.Resolved, "refund_full", false)]
    [InlineData(BookingStatus.Resolved, "refund_partial", false)]
    [InlineData(BookingStatus.Resolved, null, false)]
    [InlineData(BookingStatus.CancelledByClient, null, false)]
    [InlineData(BookingStatus.LateCancelledByClient, null, false)]
    [InlineData(BookingStatus.CancelledByPractitioner, null, false)]
    [InlineData(BookingStatus.ClientNoShow, null, false)]
    [InlineData(BookingStatus.Expired, null, false)]
    [InlineData(BookingStatus.PendingPayment, null, false)]
    [InlineData(BookingStatus.Confirmed, null, false)]
    [InlineData(BookingStatus.Disputed, null, false)]
    public async Task Reviewable_depends_on_status_and_resolution(BookingStatus status, string? resolution, bool expected)
    {
        using var h = NewHost();
        var ended = h.Clock.GetUtcNow().AddDays(-1);
        var id = await Seed(h, status, resolution, endedAt: ended);
        var r = await Get(h, id);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await Json(r);
        Assert.Equal(expected, body.GetProperty("reviewable").GetBoolean());
        Assert.Equal("demo-client-rev", body.GetProperty("clientUserId").GetString());
        Assert.Equal(h.Practitioner, body.GetProperty("practitionerId").GetGuid());
        Assert.Equal(ended, body.GetProperty("completedAt").GetDateTimeOffset());
    }

    [Theory]
    [InlineData(-1, true)]  // 1 s inside the 14 days
    [InlineData(0, true)]   // exactly 14 days: still within
    [InlineData(1, false)]  // 1 s outside
    public async Task Window_edge_is_fourteen_days_after_ended_at(int secondsPastWindow, bool expected)
    {
        using var h = NewHost();
        var id = await Seed(h, BookingStatus.Completed, endedAt: h.Clock.GetUtcNow() - Window - TimeSpan.FromSeconds(secondsPastWindow));
        Assert.Equal(expected, (await Json(await Get(h, id))).GetProperty("reviewable").GetBoolean());
    }

    [Fact]
    public async Task Without_ended_at_the_window_runs_from_end_utc()
    {
        using var h = NewHost();
        var now = h.Clock.GetUtcNow();
        var inside = await Seed(h, BookingStatus.Completed, endUtc: now - Window + TimeSpan.FromSeconds(1));
        var outside = await Seed(h, BookingStatus.Completed, endUtc: now - Window - TimeSpan.FromSeconds(1));
        var a = await Json(await Get(h, inside));
        Assert.True(a.GetProperty("reviewable").GetBoolean());
        Assert.Equal(now - Window + TimeSpan.FromSeconds(1), a.GetProperty("completedAt").GetDateTimeOffset());
        Assert.False((await Json(await Get(h, outside))).GetProperty("reviewable").GetBoolean());
    }

    [Fact]
    public async Task Ended_at_wins_over_end_utc()
    {
        using var h = NewHost();
        var now = h.Clock.GetUtcNow();
        var id = await Seed(h, BookingStatus.Completed, endedAt: now.AddDays(-1), endUtc: now.AddDays(-30));
        Assert.True((await Json(await Get(h, id))).GetProperty("reviewable").GetBoolean());
    }

    [Fact]
    public async Task Window_follows_the_clock()
    {
        using var h = NewHost();
        var id = await Seed(h, BookingStatus.Completed, endedAt: h.Clock.GetUtcNow().AddDays(-13));
        Assert.True((await Json(await Get(h, id))).GetProperty("reviewable").GetBoolean());
        h.Clock.Advance(TimeSpan.FromDays(2));
        Assert.False((await Json(await Get(h, id))).GetProperty("reviewable").GetBoolean());
    }

    [Fact]
    public async Task Unknown_booking_is_404_not_found()
    {
        using var h = NewHost();
        var r = await Get(h, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Equal("not_found", (await Json(r)).GetProperty("code").GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-key")]
    public async Task Missing_or_wrong_internal_key_is_rejected(string? key)
    {
        using var h = NewHost();
        var id = await Seed(h, BookingStatus.Completed, endedAt: h.Clock.GetUtcNow());
        var r = await Get(h, id, key);
        Assert.Contains(r.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden });
    }

    [Fact]
    public async Task A_user_token_does_not_open_the_internal_endpoint()
    {
        using var h = NewHost();
        var id = await Seed(h, BookingStatus.Completed, endedAt: h.Clock.GetUtcNow());
        var r = await h.ClientFor("demo-client-rev").GetAsync($"/internal/v1/bookings/{id}/reviewability");
        Assert.Contains(r.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden });
    }

    [Fact]
    public async Task Response_has_no_brief_and_no_price()
    {
        using var h = NewHost();
        var id = await Seed(h, BookingStatus.Completed, endedAt: h.Clock.GetUtcNow().AddDays(-1));
        var raw = await (await Get(h, id)).Content.ReadAsStringAsync();
        Assert.DoesNotContain("SECRET-TEXT", raw);
        Assert.DoesNotContain("12345", raw);
        var names = JsonDocument.Parse(raw).RootElement.EnumerateObject().Select(p => p.Name).Order().ToArray();
        Assert.Equal(new[] { "clientUserId", "completedAt", "practitionerId", "reviewable" }, names);
    }
}
