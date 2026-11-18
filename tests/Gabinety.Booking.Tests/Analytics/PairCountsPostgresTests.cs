using System.Net;
using System.Text.Json;
using Gabinety.Booking.Features.Analytics;
using Gabinety.Booking.Features.Bookings.Create;
using Gabinety.Booking.Features.Payouts;
using Gabinety.Booking.Persistence;
using Gabinety.Contracts.Booking;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using BookingEntity = Gabinety.Booking.Features.Bookings.Entities.Booking;
using BookingStatus = Gabinety.Billing.BookingStatus;

namespace Gabinety.Booking.Tests;

/// <summary>One PostgreSQL 17 and one Booking host for the whole class: every test uses its own client-id prefix.</summary>
public sealed class PairCountsPostgresFixture : IAsyncLifetime
{
    public const string InternalKey = "k-internal-pg-pair-counts";
    PostgreSqlContainer _pg = null!;
    int _day;
    internal CreateHost Host { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _pg = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await _pg.StartAsync();
        Host = new CreateHost { Configure = s => s.PostConfigure<RateLimiterOptions>(o => o.GlobalLimiter = null) }; // a walk of many pages is more than 60 requests a minute from one address
        Host.Settings["Internal:Key"] = InternalKey;
        Host.Settings["Database:Provider"] = "PostgreSQL";
        Host.Settings["ConnectionStrings:bookingdb"] = _pg.GetConnectionString();
        Host.CreateClient();
        var state = Host.Services.GetRequiredService<MigrationState>();
        await state.WaitForCompletionAsync(TimeSpan.FromSeconds(60));
        Assert.Equal(MigrationStatus.Done, state.Status);
    }

    public async Task DisposeAsync() { await Host.DisposeAsync(); await _pg.DisposeAsync(); }

    /// <summary>Seeds <paramref name="count"/> bookings of a pair, each with a payment in <paramref name="payment"/> (none when null). Every booking gets its own day, so no two overlap.</summary>
    public Task Seed(string client, Guid practitioner, int count, PaymentStatus? payment = PaymentStatus.Succeeded, BookingStatus status = BookingStatus.Completed) =>
        Host.Seed(db =>
        {
            for (var i = 0; i < count; i++)
            {
                var start = Host.Clock.GetUtcNow().AddDays(-10 - Interlocked.Increment(ref _day));
                var id = Guid.NewGuid();
                if (payment is { } ps)
                    db.Payments.Add(new Payment
                    {
                        Id = Guid.NewGuid(), BookingId = id, Provider = "mock", AmountMinor = 12345, Status = ps,
                        RefundedMinor = ps == PaymentStatus.Refunded ? 12345 : ps == PaymentStatus.PartiallyRefunded ? 100 : 0, CreatedAt = start, UpdatedAt = start,
                    });
                db.Bookings.Add(new BookingEntity
                {
                    Id = id, ClientUserId = client, PractitionerId = practitioner, PractitionerUserId = "demo-practitioner-user",
                    OfferingId = Host.Offering, OfferingTitle = "demo-offering", DurationMinutes = 60, PriceMinor = 12345,
                    StartUtc = start, EndUtc = start.AddHours(1), Status = status, BriefText = "demo-brief",
                    IdempotencyKey = Guid.NewGuid(), CreatedAt = start.AddDays(-2),
                });
            }
        });

    public async Task<JsonElement> Get(string query)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/internal/v1/analytics/pair-counts" + query);
        req.Headers.Add("X-Internal-Key", InternalKey);
        var r = await Host.CreateClient().SendAsync(req);
        var raw = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == HttpStatusCode.OK, $"{(int)r.StatusCode} {raw}");
        return JsonDocument.Parse(raw).RootElement.Clone();
    }

    /// <summary>All pages of <paramref name="limit"/> items, in the order served; the pairs of one test are told apart by <paramref name="prefix"/>.</summary>
    public async Task<(List<JsonElement> Pairs, int Pages, int LargestPage)> Walk(string prefix, int limit)
    {
        var pairs = new List<JsonElement>(); string? cursor = null; var pages = 0; var largest = 0;
        do
        {
            var page = await Get($"?limit={limit}" + (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor)));
            pages++;
            var items = page.GetProperty("items").EnumerateArray().ToList();
            largest = Math.Max(largest, items.Count);
            pairs.AddRange(items.Where(i => i.GetProperty("clientUserId").GetString()!.StartsWith(prefix, StringComparison.Ordinal)));
            cursor = page.TryGetProperty("nextCursor", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
            Assert.True(pages < 200, "the cursor never ends");
        } while (cursor is not null);
        return (pairs, pages, largest);
    }
}

/// <summary>
/// PostgreSQL proof (CI `integration` job, needs Docker) that the pair-counts query of T-10.03 translates: the correlated EXISTS over payments with the
/// enum stored as text, GROUP BY (client, practitioner) with HAVING, the keyset cursor on (text, uuid) and the hard page cap. The InMemory tests in <c>PairCountsApiTests</c> cannot show that.
/// </summary>
[Trait("tier", "integration")]
public class PairCountsPostgresTests(PairCountsPostgresFixture f) : IClassFixture<PairCountsPostgresFixture>
{
    [Fact]
    public async Task A_pair_with_more_than_two_paid_bookings_is_listed_with_its_count_and_a_pair_with_two_is_not()
    {
        var p = Guid.NewGuid();
        await f.Seed("demo-pgc-three", p, 3);
        await f.Seed("demo-pgc-two", p, 2);
        await f.Seed("demo-pgc-split", p, 2);
        await f.Seed("demo-pgc-split", Guid.NewGuid(), 2); // four bookings by one client, never more than two with one practitioner

        var (pairs, _, _) = await f.Walk("demo-pgc-", 50);

        var only = Assert.Single(pairs);
        Assert.Equal("demo-pgc-three", only.GetProperty("clientUserId").GetString());
        Assert.Equal(p, only.GetProperty("practitionerId").GetGuid());
        Assert.Equal(3, only.GetProperty("bookings").GetInt32());
    }

    [Fact]
    public async Task Only_a_succeeded_or_partially_refunded_payment_counts_the_booking_status_does_not_matter()
    {
        var p = Guid.NewGuid();
        await f.Seed("demo-pgk-ok", p, 1, PaymentStatus.Succeeded, BookingStatus.Confirmed);
        await f.Seed("demo-pgk-ok", p, 1, PaymentStatus.PartiallyRefunded, BookingStatus.Resolved);
        await f.Seed("demo-pgk-ok", p, 1, PaymentStatus.Succeeded, BookingStatus.LateCancelledByClient);
        foreach (var unpaid in new PaymentStatus?[] { null, PaymentStatus.Created, PaymentStatus.Failed, PaymentStatus.Refunded })
            await f.Seed("demo-pgk-no", p, 1, unpaid); // five bookings in all, two paid
        await f.Seed("demo-pgk-no", p, 1, PaymentStatus.Succeeded);
        await f.Seed("demo-pgk-no", p, 1, PaymentStatus.Succeeded);

        var (pairs, _, _) = await f.Walk("demo-pgk-", 50);

        var only = Assert.Single(pairs);
        Assert.Equal("demo-pgk-ok", only.GetProperty("clientUserId").GetString());
        Assert.Equal(3, only.GetProperty("bookings").GetInt32());
    }

    [Fact]
    public async Task The_pair_key_is_the_plain_form_and_the_hashed_h_form_for_a_long_client_id()
    {
        var p = Guid.NewGuid();
        var longClient = "demo-pgh-long-" + new string('x', 36); // 50 characters, the column is varchar(64): with ":" and the practitioner id the key is over 80
        await f.Seed("demo-pgh-short", p, 3);
        await f.Seed(longClient, p, 3);

        var (pairs, _, _) = await f.Walk("demo-pgh-", 50);

        var shortKey = pairs.Single(i => i.GetProperty("clientUserId").GetString() == "demo-pgh-short").GetProperty("pairKey").GetString();
        var longKey = pairs.Single(i => i.GetProperty("clientUserId").GetString() == longClient).GetProperty("pairKey").GetString()!;
        Assert.Equal($"demo-pgh-short:{p}", shortKey);
        Assert.StartsWith("h:", longKey);
        Assert.Equal(PairKey.For(longClient, p), longKey);
        Assert.True(longKey.Length <= PairKey.MaxLength);
    }

    [Fact]
    public async Task Paging_by_cursor_returns_every_pair_once_whatever_the_database_collation_does_to_the_text_order()
    {
        var p = Guid.NewGuid(); var q = Guid.NewGuid();
        // Ids that sort differently by ordinal, by case and by punctuation: the ORDER BY and the cursor's ">" are both evaluated by PostgreSQL, so they must agree.
        string[] clients = ["demo-pgo-a", "demo-pgo-B", "demo-pgo-a-1", "demo-pgo-a1", "demo-pgo-A", "demo-pgo-_a", "demo-pgo-b"];
        foreach (var c in clients) await f.Seed(c, p, 3);
        await f.Seed("demo-pgo-a", q, 3); // same client, second practitioner: the cursor's second key

        var (single, _, _) = await f.Walk("demo-pgo-", 50);
        var (paged, pages, largest) = await f.Walk("demo-pgo-", 2);

        Assert.Equal(clients.Length + 1, single.Count);
        Assert.True(pages >= 4 && largest <= 2);
        string Key(JsonElement e) => e.GetProperty("clientUserId").GetString() + "|" + e.GetProperty("practitionerId").GetGuid();
        Assert.Equal(single.Select(Key), paged.Select(Key)); // same pairs, same order, none twice, none skipped
        Assert.Equal(paged.Count, paged.Select(Key).Distinct().Count());
    }

    [Fact]
    public async Task A_huge_limit_is_capped_at_50_with_a_cursor_for_the_rest()
    {
        var p = Guid.NewGuid();
        for (var i = 0; i < 55; i++) await f.Seed($"demo-pgx-{i:D3}", p, 3);

        var first = await f.Get("?limit=100000");
        var all = await f.Walk("demo-pgx-", 100000);

        Assert.Equal(PairCountsFeature.MaxLimit, first.GetProperty("items").GetArrayLength());
        Assert.Equal(JsonValueKind.String, first.GetProperty("nextCursor").ValueKind);
        Assert.Equal(55, all.Pairs.Count);
        Assert.True(all.LargestPage <= PairCountsFeature.MaxLimit);
    }
}
