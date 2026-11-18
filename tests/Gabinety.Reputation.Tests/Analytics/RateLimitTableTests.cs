using Gabinety.ServiceDefaults;
using Microsoft.AspNetCore.Http;

namespace Gabinety.Reputation.Tests.Analytics;

/// <summary>Which CONTRACTS 10.2 row a request belongs to, and the windows of each row at the default configuration.</summary>
public class RateLimitTableTests
{
    [Theory]
    [InlineData("GET", "/api/v1/practitioners", "reads")]
    [InlineData("GET", "/api/v1/practitioners/some-slug", "reads")]
    [InlineData("GET", "/api/v1/practitioners/6f1b/slots", "reads")]
    [InlineData("GET", "/api/v1/practitioners/6f1b/reviews", "reads")]
    [InlineData("GET", "/API/V1/Taxonomy", "reads")]
    [InlineData("GET", "/api/v1/sitemap", "reads")]
    [InlineData("GET", "/api/v1/ranking-rules", "reads")]
    [InlineData("GET", "/api/v1/practitioners-archive", "other")] // a prefix is not a path segment
    [InlineData("POST", "/api/v1/practitioners", "other")]
    [InlineData("POST", "/api/v1/bookings", "bookings")]
    [InlineData("POST", "/api/v1/bookings/", "bookings")]
    [InlineData("POST", "/api/v1/bookings/6f1b/cancel", "other")]
    [InlineData("GET", "/api/v1/bookings", "other")]
    [InlineData("POST", "/api/v1/reviews", "reviews")]
    [InlineData("POST", "/api/v1/reviews/6f1b/reply", "other")]
    [InlineData("GET", "/api/v1/admin/reviews", "other")]
    [InlineData("POST", "/api/v1/assistant/brief", "other")] // the Assistant's own BriefRateLimiter holds this row
    [InlineData("POST", "/webhooks/payments/mock", "webhooks")]
    [InlineData("POST", "/webhooks/payments/stripe", "webhooks")]
    [InlineData("GET", "/internal/v1/analytics/pair-counts", "other")]
    public void A_request_belongs_to_one_row(string method, string path, string group) => Assert.Equal(group, RateLimitTable.GroupOf(method, new PathString(path)));

    [Fact]
    public void The_default_table_is_the_contract()
    {
        static (long Minutes, int Permits)[] Of(string g) => RateLimitTable.RulesOf(g, 60).Select(r => ((long)r.Window.TotalMinutes, r.Permits)).ToArray();
        Assert.Equal([(1L, 120)], Of("reads"));
        Assert.Equal([(1L, 10), (1440L, 30)], Of("bookings"));
        Assert.Equal([(60L, 5)], Of("reviews"));
        Assert.Equal([(1L, 60)], Of("other"));
    }

    [Fact]
    public void A_configured_value_is_everything_else_and_scales_the_others_only_above_the_default()
    {
        Assert.Equal(7, Assert.Single(RateLimitTable.RulesOf("other", 7)).Permits);
        Assert.Equal(120, Assert.Single(RateLimitTable.RulesOf("reads", 7)).Permits); // below the default nothing else changes
        Assert.Equal(12000, Assert.Single(RateLimitTable.RulesOf("reads", 6000)).Permits);
        Assert.Equal(6000, Assert.Single(RateLimitTable.RulesOf("other", 6000)).Permits);
        Assert.Equal([1000, 3000], RateLimitTable.RulesOf("bookings", 6000).Select(r => r.Permits).ToArray());
    }

    [Fact]
    public void A_user_id_can_never_collide_with_an_address()
    {
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.7");
        var anon = RateLimitTable.PartitionKey("reads", ctx);
        ctx.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity([new System.Security.Claims.Claim("sub", "203.0.113.7")], "t"));
        var user = RateLimitTable.PartitionKey("reads", ctx);
        Assert.NotEqual(anon, user);
        Assert.Equal("reads|a:203.0.113.7", anon);
        Assert.Equal("reads|u:203.0.113.7", user);
    }
}
