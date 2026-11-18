using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gabinety.Reputation.Tests.Moderation;

namespace Gabinety.Reputation.Tests.Analytics;

/// <summary>
/// CONTRACTS 10.2, the rows Reputation serves: public reads, <c>POST /api/v1/reviews</c> and "everything else". Every test exceeds its limit, expects 429 on the next
/// request, then moves the fake clock past the window and expects the next request to pass. The limiter runs with its default (no <c>RateLimit:PerMinute</c>),
/// except in the one test that proves the configured value scales the table.
/// </summary>
public class RateLimitTests
{
    static readonly string ReadUrl = $"/api/v1/practitioners/{Guid.NewGuid()}/reviews";
    const string AdminUrl = "/api/v1/admin/reviews";

    static async Task<HttpStatusCode> Send(HttpClient c, HttpMethod m, string url, object? body = null)
    {
        using var req = new HttpRequestMessage(m, url);
        if (body is not null) req.Content = JsonContent.Create(body);
        using var res = await c.SendAsync(req);
        return res.StatusCode;
    }

    static async Task<int> CountUntilLimited(HttpClient c, HttpMethod m, string url, int max, object? body = null)
    {
        for (var i = 0; i < max; i++) if (await Send(c, m, url, body) == HttpStatusCode.TooManyRequests) return i;
        return max;
    }

    // ---- public reads: 120 per minute

    [Fact]
    public async Task Public_reads_allow_120_a_minute_the_121st_is_429_and_the_window_recovers()
    {
        using var h = new ModerationHost();
        var anon = h.As(null);
        for (var i = 0; i < 120; i++) Assert.Equal(HttpStatusCode.OK, await Send(anon, HttpMethod.Get, ReadUrl));
        var limited = await anon.GetAsync(ReadUrl);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("rate_limited", JsonDocument.Parse(await limited.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString());
        Assert.True(int.Parse(limited.Headers.GetValues("Retry-After").Single()) is >= 1 and <= 60);
        h.Clock.Advance(TimeSpan.FromSeconds(59));
        Assert.Equal(HttpStatusCode.TooManyRequests, await Send(anon, HttpMethod.Get, ReadUrl)); // not yet
        h.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(HttpStatusCode.OK, await Send(anon, HttpMethod.Get, ReadUrl)); // the window is a minute
    }

    [Fact]
    public async Task A_signed_in_reader_is_keyed_by_the_user_id_not_the_address()
    {
        using var h = new ModerationHost();
        var alice = h.As("demo-rl-alice");
        Assert.Equal(120, await CountUntilLimited(alice, HttpMethod.Get, ReadUrl, 130));
        Assert.Equal(HttpStatusCode.OK, await Send(h.As("demo-rl-bob"), HttpMethod.Get, ReadUrl)); // same address, other user
        Assert.Equal(HttpStatusCode.OK, await Send(h.As(null), HttpMethod.Get, ReadUrl)); // the anonymous address bucket is untouched
    }

    [Fact]
    public async Task A_read_does_not_spend_the_write_budget_and_the_other_way_round()
    {
        using var h = new ModerationHost();
        var u = h.As("demo-rl-split");
        Assert.Equal(120, await CountUntilLimited(u, HttpMethod.Get, ReadUrl, 130));
        Assert.NotEqual(HttpStatusCode.TooManyRequests, await Send(u, HttpMethod.Post, "/api/v1/reviews", new { })); // separate group
    }

    // ---- POST /api/v1/reviews: 5 per hour per user

    [Fact]
    public async Task Posting_reviews_allows_5_an_hour_the_6th_is_429_and_the_hour_recovers()
    {
        using var h = new ModerationHost();
        var u = h.As("demo-rl-reviewer");
        for (var i = 0; i < 5; i++) Assert.NotEqual(HttpStatusCode.TooManyRequests, await Send(u, HttpMethod.Post, "/api/v1/reviews", new { })); // rejected as invalid, but counted
        Assert.Equal(HttpStatusCode.TooManyRequests, await Send(u, HttpMethod.Post, "/api/v1/reviews", new { }));
        Assert.NotEqual(HttpStatusCode.TooManyRequests, await Send(h.As("demo-rl-reviewer-2"), HttpMethod.Post, "/api/v1/reviews", new { })); // per user
        h.Clock.Advance(TimeSpan.FromMinutes(59));
        Assert.Equal(HttpStatusCode.TooManyRequests, await Send(u, HttpMethod.Post, "/api/v1/reviews", new { }));
        h.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.NotEqual(HttpStatusCode.TooManyRequests, await Send(u, HttpMethod.Post, "/api/v1/reviews", new { }));
    }

    [Fact]
    public async Task A_refused_review_post_is_not_counted_so_the_hour_does_not_keep_extending()
    {
        using var h = new ModerationHost();
        var u = h.As("demo-rl-nocount");
        for (var i = 0; i < 5; i++) await Send(u, HttpMethod.Post, "/api/v1/reviews", new { });
        for (var i = 0; i < 20; i++) { h.Clock.Advance(TimeSpan.FromMinutes(2)); Assert.Equal(HttpStatusCode.TooManyRequests, await Send(u, HttpMethod.Post, "/api/v1/reviews", new { })); }
        h.Clock.Advance(TimeSpan.FromMinutes(21)); // 61 minutes after the first five
        Assert.NotEqual(HttpStatusCode.TooManyRequests, await Send(u, HttpMethod.Post, "/api/v1/reviews", new { }));
    }

    // ---- everything else: 60 per minute

    [Fact]
    public async Task Everything_else_allows_60_a_minute_per_user_the_61st_is_429_and_the_window_recovers()
    {
        using var h = new ModerationHost();
        var admin = h.As("demo-rl-admin", "Admin");
        for (var i = 0; i < 60; i++) Assert.Equal(HttpStatusCode.OK, await Send(admin, HttpMethod.Get, AdminUrl));
        Assert.Equal(HttpStatusCode.TooManyRequests, await Send(admin, HttpMethod.Get, AdminUrl));
        Assert.Equal(HttpStatusCode.OK, await Send(h.As("demo-rl-admin-2", "Admin"), HttpMethod.Get, AdminUrl)); // keyed by user
        h.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(HttpStatusCode.OK, await Send(admin, HttpMethod.Get, AdminUrl));
    }

    [Fact]
    public async Task A_caller_without_a_token_is_keyed_by_address_and_gets_401_until_the_61st_which_is_429()
    {
        using var h = new ModerationHost();
        var anon = h.As(null);
        for (var i = 0; i < 60; i++) Assert.Equal(HttpStatusCode.Unauthorized, await Send(anon, HttpMethod.Get, AdminUrl));
        Assert.Equal(HttpStatusCode.TooManyRequests, await Send(anon, HttpMethod.Get, AdminUrl));
    }

    // ---- configuration

    [Fact]
    public async Task The_configured_per_minute_value_scales_the_whole_table_only_when_it_is_above_the_default()
    {
        using var h = new ModerationHost();
        h.Settings["RateLimit:PerMinute"] = "120";
        var admin = h.As("demo-rl-scaled", "Admin");
        Assert.Equal(120, await CountUntilLimited(admin, HttpMethod.Get, AdminUrl, 130)); // everything else: the value itself
        var u = h.As("demo-rl-scaled-reviewer");
        Assert.Equal(10, await CountUntilLimited(u, HttpMethod.Post, "/api/v1/reviews", 20, new { })); // 5 an hour doubled
    }
}
