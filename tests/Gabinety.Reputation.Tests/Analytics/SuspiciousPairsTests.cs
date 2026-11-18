using System.Net;
using System.Text.Json;
using Gabinety.Contracts.Reputation;
using Gabinety.Reputation.Persistence;

namespace Gabinety.Reputation.Tests.Analytics;

/// <summary>GET /api/v1/admin/suspicious-pairs (T-10.03, SPEC 11): more than two paid bookings and ONLY five-star reviews.</summary>
public class SuspiciousPairsTests
{
    const string Url = "/api/v1/admin/suspicious-pairs";

    static async Task<JsonElement> Body(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();
    static string[] Clients(JsonElement page) => page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("clientUserId").GetString()!).ToArray();

    [Fact]
    public async Task Matches_the_constructed_case_and_ignores_a_pair_with_one_4_star_review()
    {
        using var h = new AnalyticsHost();
        var p = Guid.NewGuid();
        // farmer: 3 bookings, 3 reviews, all 5 stars -> listed
        // mixed: 3 bookings, 5 + 5 + 4 -> ignored (one 4-star review is enough to clear the pair)
        // lone4: 3 bookings, one 4-star review -> ignored
        // silent: 3 bookings, no review -> ignored (nothing to inflate)
        await h.Seed(
            AnalyticsHost.NewReview("demo-ap-farmer", p, 5), AnalyticsHost.NewReview("demo-ap-farmer", p, 5), AnalyticsHost.NewReview("demo-ap-farmer", p, 5),
            AnalyticsHost.NewReview("demo-ap-mixed", p, 5), AnalyticsHost.NewReview("demo-ap-mixed", p, 5), AnalyticsHost.NewReview("demo-ap-mixed", p, 4),
            AnalyticsHost.NewReview("demo-ap-lone4", p, 4));
        h.Booking.Add(AnalyticsHost.Pair("demo-ap-farmer", p, 3), AnalyticsHost.Pair("demo-ap-lone4", p, 3), AnalyticsHost.Pair("demo-ap-mixed", p, 3), AnalyticsHost.Pair("demo-ap-silent", p, 3));

        var r = await h.As("demo-admin", "Admin").GetAsync(Url);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var page = await Body(r);
        var item = Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.Equal("demo-ap-farmer", item.GetProperty("clientUserId").GetString());
        Assert.Equal(p, item.GetProperty("practitionerId").GetGuid());
        Assert.Equal($"demo-ap-farmer:{p}", item.GetProperty("pairKey").GetString()); // Booking's key, passed through
        Assert.Equal(3, item.GetProperty("bookings").GetInt32());
        Assert.Equal(3, item.GetProperty("reviews").GetInt32());
    }

    [Fact]
    public async Task One_review_of_five_stars_on_a_pair_with_three_bookings_is_listed()
    {
        using var h = new AnalyticsHost();
        var p = Guid.NewGuid();
        await h.Seed(AnalyticsHost.NewReview("demo-ap-one5", p, 5));
        h.Booking.Add(AnalyticsHost.Pair("demo-ap-one5", p, 3));
        Assert.Equal(["demo-ap-one5"], Clients(await Body(await h.As("demo-admin", "Admin").GetAsync(Url))));
    }

    [Fact]
    public async Task The_join_is_by_the_pair_not_by_the_client_or_the_practitioner_alone()
    {
        using var h = new AnalyticsHost();
        var x = Guid.NewGuid(); var y = Guid.NewGuid();
        // The client gave 5 stars to X and 4 to Y: the pair (client, Y) must not borrow X's reviews, and the pair (client, X) must not see Y's 4.
        await h.Seed(AnalyticsHost.NewReview("demo-ap-cross", x, 5), AnalyticsHost.NewReview("demo-ap-cross", y, 4), AnalyticsHost.NewReview("demo-ap-other", y, 5));
        h.Booking.Add(AnalyticsHost.Pair("demo-ap-cross", x, 3), AnalyticsHost.Pair("demo-ap-cross", y, 3), AnalyticsHost.Pair("demo-ap-other", x, 3));
        var items = (await Body(await h.As("demo-admin", "Admin").GetAsync(Url))).GetProperty("items").EnumerateArray().ToList();
        var only = Assert.Single(items); // (cross, X): 5 stars. (cross, Y): a 4. (other, X): no review of its own
        Assert.Equal("demo-ap-cross", only.GetProperty("clientUserId").GetString());
        Assert.Equal(x, only.GetProperty("practitionerId").GetGuid());
    }

    [Fact]
    public async Task A_held_five_star_review_counts_and_a_rejected_one_does_not()
    {
        using var h = new AnalyticsHost();
        var p = Guid.NewGuid();
        await h.Seed(
            AnalyticsHost.NewReview("demo-ap-held", p, 5, ReviewStatus.Held),
            AnalyticsHost.NewReview("demo-ap-rej", p, 5, ReviewStatus.Rejected),
            AnalyticsHost.NewReview("demo-ap-rej4", p, 5), AnalyticsHost.NewReview("demo-ap-rej4", p, 4, ReviewStatus.Rejected));
        h.Booking.Add(AnalyticsHost.Pair("demo-ap-held", p, 3), AnalyticsHost.Pair("demo-ap-rej", p, 3), AnalyticsHost.Pair("demo-ap-rej4", p, 3));
        var clients = Clients(await Body(await h.As("demo-admin", "Admin").GetAsync(Url)));
        Assert.Equal(["demo-ap-held", "demo-ap-rej4"], clients.Order().ToArray()); // a rejected review is not a live one: it neither lists nor clears a pair
    }

    [Fact]
    public async Task A_pair_the_booking_side_reports_with_two_bookings_is_ignored_even_with_five_stars()
    {
        using var h = new AnalyticsHost();
        var p = Guid.NewGuid();
        await h.Seed(AnalyticsHost.NewReview("demo-ap-two", p, 5));
        h.Booking.Add(AnalyticsHost.Pair("demo-ap-two", p, 2));
        Assert.Empty(Clients(await Body(await h.As("demo-admin", "Admin").GetAsync(Url))));
    }

    [Fact]
    public async Task Asks_Booking_for_more_than_two_bookings_with_the_page_limit_and_never_more_than_50()
    {
        using var h = new AnalyticsHost();
        h.Booking.Add();
        await h.As("demo-admin", "Admin").GetAsync(Url);
        await h.As("demo-admin", "Admin").GetAsync(Url + "?limit=9999");
        Assert.Equal((3, (string?)null, 20), h.Booking.Calls[0]);
        Assert.Equal((3, (string?)null, 50), h.Booking.Calls[1]);
    }

    [Fact]
    public async Task The_answer_has_ids_and_counts_only_never_review_text()
    {
        using var h = new AnalyticsHost();
        var p = Guid.NewGuid();
        await h.Seed(AnalyticsHost.NewReview("demo-ap-priv", p, 5));
        h.Booking.Add(AnalyticsHost.Pair("demo-ap-priv", p, 3));
        var r = await h.As("demo-admin", "Admin").GetAsync(Url);
        var raw = await r.Content.ReadAsStringAsync();
        Assert.DoesNotContain("PARY-TEKST", raw);
        var item = (await Body(r)).GetProperty("items")[0];
        Assert.Equal(["bookings", "clientUserId", "pairKey", "practitionerId", "reviews"], item.EnumerateObject().Select(x => x.Name).Order().ToArray());
        Assert.DoesNotContain(h.Logs.Lines, l => l.Contains("PARY-TEKST"));
    }

    [Fact]
    public async Task Passes_the_cursor_through_and_keeps_scanning_up_to_five_pages_until_something_matches()
    {
        using var h = new AnalyticsHost();
        var p = Guid.NewGuid();
        await h.Seed(AnalyticsHost.NewReview("demo-ap-deep", p, 5));
        for (var i = 0; i < 2; i++) h.Booking.Add(AnalyticsHost.Pair($"demo-ap-nothing{i}", p, 3)); // pages 0 and 1: nobody reviewed
        h.Booking.Add(AnalyticsHost.Pair("demo-ap-deep", p, 3)); // page 2 matches
        h.Booking.Add(AnalyticsHost.Pair("demo-ap-later", p, 3)); // page 3 is not read: one match is enough for a response
        var page = await Body(await h.As("demo-admin", "Admin").GetAsync(Url));
        Assert.Equal(["demo-ap-deep"], Clients(page));
        Assert.Equal("p3", page.GetProperty("nextCursor").GetString());
        Assert.Equal(new string?[] { null, "p1", "p2" }, h.Booking.Calls.Select(c => c.Cursor).ToArray());

        var next = await Body(await h.As("demo-admin", "Admin").GetAsync(Url + "?cursor=p3"));
        Assert.Equal("p3", h.Booking.Calls[^1].Cursor);
        Assert.False(next.TryGetProperty("nextCursor", out var n) && n.ValueKind == JsonValueKind.String);
    }

    [Fact]
    public async Task Stops_after_five_empty_pages_and_hands_the_admin_the_cursor_to_continue()
    {
        using var h = new AnalyticsHost();
        var p = Guid.NewGuid();
        for (var i = 0; i < 8; i++) h.Booking.Add(AnalyticsHost.Pair($"demo-ap-empty{i}", p, 3));
        var page = await Body(await h.As("demo-admin", "Admin").GetAsync(Url));
        Assert.Empty(Clients(page));
        Assert.Equal(5, h.Booking.Calls.Count); // a hard cap on the work of one request
        Assert.Equal("p5", page.GetProperty("nextCursor").GetString());
    }

    [Theory]
    [InlineData("?limit=0")]
    [InlineData("?limit=-5")]
    [InlineData("?cursor=has%20space")]
    [InlineData("?cursor=%3Cscript%3E")]
    public async Task A_bad_limit_or_cursor_is_400_and_Booking_is_not_asked(string query)
    {
        using var h = new AnalyticsHost();
        h.Booking.Add();
        var r = await h.As("demo-admin", "Admin").GetAsync(Url + query);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("validation_failed", (await Body(r)).GetProperty("code").GetString());
        Assert.Empty(h.Booking.Calls);
    }

    [Fact]
    public async Task Booking_unavailable_is_503_dependency_unavailable_with_no_detail()
    {
        using var h = new AnalyticsHost();
        h.Booking.Unavailable = true;
        var r = await h.As("demo-admin", "Admin").GetAsync(Url);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode);
        var body = await Body(r);
        Assert.Equal("dependency_unavailable", body.GetProperty("code").GetString());
        Assert.DoesNotContain("booking", (await r.Content.ReadAsStringAsync()).Replace("dependency_unavailable", ""), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("User", HttpStatusCode.Forbidden)]
    [InlineData("Practitioner", HttpStatusCode.Forbidden)]
    [InlineData("", HttpStatusCode.Forbidden)]
    public async Task Only_an_admin_gets_the_list_and_Booking_is_never_asked_for_anyone_else(string? role, HttpStatusCode expected)
    {
        using var h = new AnalyticsHost();
        h.Booking.Add();
        var c = role is null ? h.As(null) : h.As("demo-user", role.Length == 0 ? null : role);
        Assert.Equal(expected, (await c.GetAsync(Url)).StatusCode);
        Assert.Empty(h.Booking.Calls);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("SuperAdmin")]
    public async Task Both_admin_roles_may_read_it(string role)
    {
        using var h = new AnalyticsHost();
        h.Booking.Add();
        Assert.Equal(HttpStatusCode.OK, (await h.As("demo-admin", role).GetAsync(Url)).StatusCode);
    }
}
