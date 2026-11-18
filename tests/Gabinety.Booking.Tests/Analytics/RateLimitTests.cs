using System.Net;
using System.Net.Http.Json;
using System.Text;

namespace Gabinety.Booking.Tests;

/// <summary>
/// CONTRACTS 10.2, the rows Booking serves: <c>POST /api/v1/bookings</c> (10 per minute AND 30 per day per user) and the webhooks (no limit by address; the 64 KB body
/// limit is proven in <c>PaymentWebhookTests</c>). A booking POST with an empty body is refused as invalid by the handler, but the limiter has already counted it.
/// Default limiter configuration; the fake clock moves the windows.
/// </summary>
public class RateLimitTests
{
    static Task<HttpResponseMessage> PostBooking(HttpClient c) => c.PostAsJsonAsync("/api/v1/bookings", new { });

    static async Task<HttpStatusCode> Code(Task<HttpResponseMessage> r) { using var res = await r; return res.StatusCode; }

    [Fact]
    public async Task Bookings_allow_10_a_minute_the_11th_is_429_per_user_and_the_minute_recovers()
    {
        using var h = new CreateHost();
        var u = h.ClientFor("demo-rl-booker");
        for (var i = 0; i < 10; i++) Assert.NotEqual(HttpStatusCode.TooManyRequests, await Code(PostBooking(u)));
        var limited = await PostBooking(u);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Contains("\"code\":\"rate_limited\"", await limited.Content.ReadAsStringAsync());
        Assert.NotEqual(HttpStatusCode.TooManyRequests, await Code(PostBooking(h.ClientFor("demo-rl-booker-2")))); // another user, same address
        h.Clock.Advance(TimeSpan.FromSeconds(59));
        Assert.Equal(HttpStatusCode.TooManyRequests, await Code(PostBooking(u)));
        h.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.NotEqual(HttpStatusCode.TooManyRequests, await Code(PostBooking(u)));
    }

    [Fact]
    public async Task Bookings_allow_30_a_day_the_31st_is_429_even_when_the_minute_is_free_and_the_day_recovers()
    {
        using var h = new CreateHost();
        var u = h.ClientFor("demo-rl-daily");
        for (var batch = 0; batch < 3; batch++)
        {
            for (var i = 0; i < 10; i++) Assert.NotEqual(HttpStatusCode.TooManyRequests, await Code(PostBooking(u)));
            h.Clock.Advance(TimeSpan.FromMinutes(1)); // the per-minute window is clear after each batch
        }
        Assert.Equal(HttpStatusCode.TooManyRequests, await Code(PostBooking(u))); // 30 in the last 3 minutes: the day decides
        h.Clock.Advance(TimeSpan.FromHours(23));
        Assert.Equal(HttpStatusCode.TooManyRequests, await Code(PostBooking(u))); // 23 h 57 min after the first batch
        h.Clock.Advance(TimeSpan.FromHours(1));
        Assert.NotEqual(HttpStatusCode.TooManyRequests, await Code(PostBooking(u))); // the first batch has left the day
    }

    [Fact]
    public async Task Webhooks_are_never_limited_by_address()
    {
        using var h = new CreateHost();
        var anon = h.ClientFor(null);
        for (var i = 0; i < 200; i++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/webhooks/payments/mock") { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
            using var res = await anon.SendAsync(req);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, res.StatusCode);
        }
    }

    [Fact]
    public async Task Everything_else_is_60_a_minute_and_reads_of_the_slots_are_public_reads_with_their_own_budget()
    {
        using var h = new CreateHost();
        var anon = h.ClientFor(null);
        var slots = $"/api/v1/practitioners/{h.Practitioner}/slots?offeringId={h.Offering}&from=2026-11-17&to=2026-11-18";
        for (var i = 0; i < 120; i++) Assert.NotEqual(HttpStatusCode.TooManyRequests, await Code(anon.GetAsync(slots)));
        Assert.Equal(HttpStatusCode.TooManyRequests, await Code(anon.GetAsync(slots))); // reads: 120, not the 60 of the rest
        var me = h.ClientFor("demo-rl-me");
        for (var i = 0; i < 60; i++) Assert.NotEqual(HttpStatusCode.TooManyRequests, await Code(me.GetAsync("/api/v1/me/bookings")));
        Assert.Equal(HttpStatusCode.TooManyRequests, await Code(me.GetAsync("/api/v1/me/bookings")));
        h.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.NotEqual(HttpStatusCode.TooManyRequests, await Code(me.GetAsync("/api/v1/me/bookings")));
        Assert.NotEqual(HttpStatusCode.TooManyRequests, await Code(anon.GetAsync(slots)));
    }
}
