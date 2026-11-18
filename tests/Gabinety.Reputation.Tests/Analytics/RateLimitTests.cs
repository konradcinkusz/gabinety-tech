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

    // ---- the client address behind the web server (BFF)

    const string EdgeKey = "k-demo-edge-DO-NOT-LEAK";

    static async Task<HttpStatusCode> Read(HttpClient c, string? address, string? key = EdgeKey)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, ReadUrl);
        if (address is not null) req.Headers.Add("X-Client-Address", address);
        if (key is not null) req.Headers.Add("X-Edge-Key", key);
        using var res = await c.SendAsync(req);
        return res.StatusCode;
    }

    static async Task<int> ReadsUntilLimited(HttpClient c, string? address, string? key = EdgeKey)
    {
        for (var i = 0; i < 130; i++) if (await Read(c, address, key) == HttpStatusCode.TooManyRequests) return i;
        return 130;
    }

    [Fact]
    public async Task With_the_trusted_proxy_key_each_forwarded_address_has_its_own_public_read_budget()
    {
        using var h = new ModerationHost();
        h.Settings["RateLimit:TrustedProxyKey"] = EdgeKey;
        var web = h.As(null);
        Assert.Equal(120, await ReadsUntilLimited(web, "203.0.113.10"));
        Assert.Equal(HttpStatusCode.OK, await Read(web, "203.0.113.11")); // another visitor behind the same web machine
        Assert.Equal(HttpStatusCode.TooManyRequests, await Read(web, "203.0.113.10"));
        h.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(HttpStatusCode.OK, await Read(web, "203.0.113.10"));
    }

    [Fact]
    public async Task The_same_address_written_two_ways_is_one_visitor()
    {
        using var h = new ModerationHost();
        h.Settings["RateLimit:TrustedProxyKey"] = EdgeKey;
        var web = h.As(null);
        for (var i = 0; i < 60; i++) await Read(web, "2001:db8::1");
        for (var i = 0; i < 60; i++) await Read(web, "2001:0db8:0000:0000:0000:0000:0000:0001");
        Assert.Equal(HttpStatusCode.TooManyRequests, await Read(web, "2001:db8:0:0:0:0:0:1")); // 120 spent under three spellings
        for (var i = 0; i < 120; i++) await Read(web, "::ffff:203.0.113.20");
        Assert.Equal(HttpStatusCode.TooManyRequests, await Read(web, "203.0.113.20")); // an IPv4-mapped address is the IPv4 address
    }

    [Fact]
    public async Task A_spoofed_address_without_the_key_is_ignored_so_rotating_it_buys_nothing()
    {
        using var h = new ModerationHost();
        h.Settings["RateLimit:TrustedProxyKey"] = EdgeKey;
        var anon = h.As(null);
        int ok = 0;
        for (var i = 0; i < 130; i++) if (await Read(anon, $"198.51.100.{i % 250}", key: null) == HttpStatusCode.OK) ok++;
        Assert.Equal(120, ok); // every request counted against the real address
    }

    [Theory]
    [InlineData("wrong-key")]
    [InlineData("")]
    public async Task A_wrong_key_is_ignored_too(string key)
    {
        using var h = new ModerationHost();
        h.Settings["RateLimit:TrustedProxyKey"] = EdgeKey;
        var anon = h.As(null);
        int ok = 0;
        for (var i = 0; i < 130; i++) if (await Read(anon, $"198.51.100.{i % 250}", key) == HttpStatusCode.OK) ok++;
        Assert.Equal(120, ok);
    }

    [Fact]
    public async Task When_the_service_has_no_trusted_proxy_key_the_header_is_never_trusted()
    {
        using var h = new ModerationHost();
        var anon = h.As(null);
        int ok = 0;
        for (var i = 0; i < 130; i++) if (await Read(anon, $"198.51.100.{i % 250}", EdgeKey) == HttpStatusCode.OK) ok++;
        Assert.Equal(120, ok);
    }

    [Theory]
    [InlineData("not-an-ip")]
    [InlineData("203.0.113.1, 203.0.113.2")]
    [InlineData("203.0.113.1:8080")]
    [InlineData("")]
    public async Task A_forwarded_value_that_is_not_one_address_falls_back_to_the_connection_address(string forwarded)
    {
        using var h = new ModerationHost();
        h.Settings["RateLimit:TrustedProxyKey"] = EdgeKey;
        var web = h.As(null);
        for (var i = 0; i < 120; i++) await Read(web, null); // the connection address spends the budget
        Assert.Equal(HttpStatusCode.TooManyRequests, await Read(web, forwarded));
    }

    [Fact]
    public async Task A_signed_in_user_keeps_the_user_id_key_whatever_address_is_forwarded()
    {
        using var h = new ModerationHost();
        h.Settings["RateLimit:TrustedProxyKey"] = EdgeKey;
        var alice = h.As("demo-rl-fwd-alice");
        for (var i = 0; i < 60; i++) await Read(alice, "203.0.113.30");
        for (var i = 0; i < 60; i++) await Read(alice, "203.0.113.31");
        Assert.Equal(HttpStatusCode.TooManyRequests, await Read(alice, "203.0.113.32")); // one user, one budget
    }
}
