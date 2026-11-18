using System.Net;
using System.Text.Json;
using Gabinety.Booking.Features.Bookings.Create;
using Gabinety.Booking.Features.Payouts;
using Gabinety.Contracts.Booking;
using BookingEntity = Gabinety.Booking.Features.Bookings.Entities.Booking;
using BookingStatus = Gabinety.Billing.BookingStatus;

namespace Gabinety.Booking.Tests;

/// <summary>GET /internal/v1/analytics/pair-counts (T-10.03): practitioner-client pairs with at least <c>minBookings</c> paid bookings, for Reputation to join with its reviews.</summary>
public class PairCountsApiTests
{
    const string InternalKey = "k-internal-DO-NOT-LEAK-9012";

    static CreateHost NewHost()
    {
        var h = new CreateHost();
        h.Settings["Internal:Key"] = InternalKey;
        return h;
    }

    static async Task<HttpResponseMessage> Get(CreateHost h, string query = "", string? key = InternalKey)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/internal/v1/analytics/pair-counts" + query);
        if (key != null) req.Headers.Add("X-Internal-Key", key);
        return await h.CreateClient().SendAsync(req);
    }

    static async Task<JsonElement> Json(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    /// <summary>Seeds <paramref name="count"/> bookings of a pair, each with a payment in <paramref name="payment"/> (none when null).</summary>
    static Task Seed(CreateHost h, string client, Guid practitioner, int count, BookingStatus status = BookingStatus.Completed, PaymentStatus? payment = PaymentStatus.Succeeded) =>
        h.Seed(db =>
        {
            for (var i = 0; i < count; i++)
            {
                var start = h.Clock.GetUtcNow().AddDays(-10 - i);
                var id = Guid.NewGuid();
                if (payment is { } ps)
                    db.Payments.Add(new Payment
                    {
                        Id = Guid.NewGuid(), BookingId = id, Provider = "mock", AmountMinor = 12345, Status = ps,
                        RefundedMinor = ps == PaymentStatus.Refunded ? 12345 : ps == PaymentStatus.PartiallyRefunded ? 100 : 0,
                        CreatedAt = start, UpdatedAt = start,
                    });
                db.Bookings.Add(new BookingEntity
                {
                    Id = id, ClientUserId = client, PractitionerId = practitioner, PractitionerUserId = "demo-practitioner-user",
                    OfferingId = h.Offering, OfferingTitle = "demo-offering", DurationMinutes = 60, PriceMinor = 12345,
                    StartUtc = start, EndUtc = start.AddHours(1), Status = status, BriefText = "demo-brief-SECRET-TEXT",
                    IdempotencyKey = Guid.NewGuid(), CreatedAt = start.AddDays(-2),
                });
            }
        });

    static List<JsonElement> Items(JsonElement page, string clientPrefix) =>
        page.GetProperty("items").EnumerateArray().Where(i => i.GetProperty("clientUserId").GetString()!.StartsWith(clientPrefix, StringComparison.Ordinal)).ToList();

    [Fact]
    public async Task Lists_a_pair_with_three_bookings_and_not_one_with_two()
    {
        using var h = NewHost();
        var p = Guid.NewGuid();
        await Seed(h, "demo-pc-three", p, 3);
        await Seed(h, "demo-pc-two", p, 2);
        var items = Items(await Json(await Get(h, "?limit=50")), "demo-pc-");
        var only = Assert.Single(items);
        Assert.Equal("demo-pc-three", only.GetProperty("clientUserId").GetString());
        Assert.Equal(p, only.GetProperty("practitionerId").GetGuid());
        Assert.Equal(3, only.GetProperty("bookings").GetInt32());
    }

    [Fact]
    public async Task The_same_client_with_two_practitioners_is_two_pairs_counted_separately()
    {
        using var h = NewHost();
        var p1 = Guid.NewGuid(); var p2 = Guid.NewGuid();
        await Seed(h, "demo-pc-split", p1, 2);
        await Seed(h, "demo-pc-split", p2, 2); // four bookings by one client, but never more than two with one practitioner
        Assert.Empty(Items(await Json(await Get(h, "?limit=50")), "demo-pc-split"));
    }

    // "Paid" is decided by the payment, not by the booking's status: a payment Succeeded or PartiallyRefunded. A payment that was never made (none, Created, Failed)
    // or was refunded in full costs the farmer nothing and does not count.
    [Theory]
    [InlineData(null)]
    [InlineData(PaymentStatus.Created)]
    [InlineData(PaymentStatus.Failed)]
    [InlineData(PaymentStatus.Refunded)]
    public async Task A_booking_without_a_kept_payment_does_not_count(PaymentStatus? payment)
    {
        using var h = NewHost();
        var p = Guid.NewGuid();
        var client = $"demo-pc-unpaid-{payment?.ToString() ?? "none"}";
        await Seed(h, client, p, 2);
        await Seed(h, client, p, 5, BookingStatus.Completed, payment);
        Assert.Empty(Items(await Json(await Get(h, "?limit=50")), client));
    }

    [Theory]
    [InlineData(PaymentStatus.Succeeded, BookingStatus.Completed)]
    [InlineData(PaymentStatus.Succeeded, BookingStatus.Confirmed)]
    [InlineData(PaymentStatus.Succeeded, BookingStatus.LateCancelledByClient)]
    [InlineData(PaymentStatus.PartiallyRefunded, BookingStatus.Resolved)]
    [InlineData(PaymentStatus.PartiallyRefunded, BookingStatus.Completed)]
    public async Task A_booking_with_a_succeeded_or_partially_refunded_payment_counts_whatever_its_status(PaymentStatus payment, BookingStatus status)
    {
        using var h = NewHost();
        var p = Guid.NewGuid();
        var client = $"demo-pc-paid-{payment}-{status}";
        await Seed(h, client, p, 3, status, payment);
        Assert.Equal(3, Assert.Single(Items(await Json(await Get(h, "?limit=50")), client)).GetProperty("bookings").GetInt32());
    }

    [Fact]
    public async Task Refunded_bookings_are_subtracted_from_the_count_not_the_whole_pair()
    {
        using var h = NewHost();
        var p = Guid.NewGuid();
        await Seed(h, "demo-pc-mixedpay", p, 2);
        await Seed(h, "demo-pc-mixedpay", p, 4, BookingStatus.CancelledByClient, PaymentStatus.Refunded);
        Assert.Empty(Items(await Json(await Get(h, "?limit=50")), "demo-pc-mixedpay")); // 2 kept + 4 refunded: two bookings, not six
        await Seed(h, "demo-pc-mixedpay", p, 1);
        Assert.Equal(3, Assert.Single(Items(await Json(await Get(h, "?limit=50")), "demo-pc-mixedpay")).GetProperty("bookings").GetInt32());
    }

    [Fact]
    public async Task The_pair_key_is_the_fee_hook_key_and_the_hashed_form_for_a_long_client_id()
    {
        using var h = NewHost();
        var p = Guid.NewGuid();
        var longClient = "demo-pc-long-" + new string('x', 70);
        await Seed(h, "demo-pc-short", p, 3);
        await Seed(h, longClient, p, 3);
        var items = (await Json(await Get(h, "?limit=50"))).GetProperty("items").EnumerateArray().ToList();
        var shortKey = items.Single(i => i.GetProperty("clientUserId").GetString() == "demo-pc-short").GetProperty("pairKey").GetString();
        var longKey = items.Single(i => i.GetProperty("clientUserId").GetString() == longClient).GetProperty("pairKey").GetString()!;
        Assert.Equal($"demo-pc-short:{p}", shortKey);
        Assert.Equal(PairKey.For("demo-pc-short", p), shortKey);
        Assert.StartsWith("h:", longKey);
        Assert.Equal(PairKey.For(longClient, p), longKey);
    }

    [Fact]
    public async Task Pages_with_an_opaque_cursor_in_a_total_order_and_the_limit_is_capped()
    {
        using var h = NewHost();
        var p = Guid.NewGuid();
        foreach (var c in new[] { "demo-pg-a", "demo-pg-b", "demo-pg-c", "demo-pg-d", "demo-pg-e" }) await Seed(h, c, p, 3);
        var seen = new List<string>(); string? cursor = null;
        do
        {
            var page = await Json(await Get(h, "?limit=2" + (cursor is null ? "" : $"&cursor={cursor}")));
            Assert.True(page.GetProperty("items").GetArrayLength() <= 2);
            seen.AddRange(Items(page, "demo-pg-").Select(i => i.GetProperty("clientUserId").GetString()!));
            cursor = page.TryGetProperty("nextCursor", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
        } while (cursor is not null);
        Assert.Equal(["demo-pg-a", "demo-pg-b", "demo-pg-c", "demo-pg-d", "demo-pg-e"], seen);

        var big = await Json(await Get(h, "?limit=100000"));
        Assert.True(big.GetProperty("items").GetArrayLength() <= 50); // hard cap of the contract's admin lists
    }

    [Theory]
    [InlineData("?limit=0")]
    [InlineData("?limit=-1")]
    [InlineData("?cursor=%21%21")]
    [InlineData("?minBookings=1")]
    [InlineData("?minBookings=abc")]
    public async Task A_bad_query_is_400_validation_failed(string query)
    {
        using var h = NewHost();
        var r = await Get(h, query);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("validation_failed", (await Json(r)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task The_threshold_is_a_parameter()
    {
        using var h = NewHost();
        var p = Guid.NewGuid();
        await Seed(h, "demo-pt-five", p, 5);
        await Seed(h, "demo-pt-four", p, 4);
        var items = Items(await Json(await Get(h, "?limit=50&minBookings=5")), "demo-pt-");
        Assert.Equal("demo-pt-five", Assert.Single(items).GetProperty("clientUserId").GetString());
    }

    [Fact]
    public async Task The_answer_carries_ids_and_counts_only_no_brief_price_or_time()
    {
        using var h = NewHost();
        var p = Guid.NewGuid();
        await Seed(h, "demo-pc-priv", p, 3);
        var r = await Get(h, "?limit=50");
        var raw = await r.Content.ReadAsStringAsync();
        Assert.DoesNotContain("SECRET-TEXT", raw);
        Assert.DoesNotContain("12345", raw);
        var item = Items(await Json(r), "demo-pc-priv").Single();
        Assert.Equal(["bookings", "clientUserId", "pairKey", "practitionerId"], item.EnumerateObject().Select(x => x.Name).Order().ToArray());
    }

    [Fact]
    public async Task Needs_the_internal_key_a_user_token_even_an_admin_one_is_not_enough()
    {
        using var h = NewHost();
        Assert.Equal(HttpStatusCode.Unauthorized, (await Get(h, key: null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Get(h, key: "wrong-key")).StatusCode);
        var admin = h.ClientFor("demo-admin", "Admin");
        Assert.Equal(HttpStatusCode.Unauthorized, (await admin.GetAsync("/internal/v1/analytics/pair-counts")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Get(h)).StatusCode);
    }
}
