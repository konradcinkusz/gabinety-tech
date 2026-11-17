using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Gabinety.Booking.Features.Payments;
using Gabinety.Contracts.Booking;
using Microsoft.EntityFrameworkCore;

namespace Gabinety.Booking.Tests;

/// <summary>GET /api/v1/admin/ledger.csv (CONTRACTS 4.3): the header, the rows, the inclusive UTC date range, order, no personal data, validation, auth, streaming.</summary>
public class LedgerExportTests
{
    const string Header = "booking_id,kind,amount_minor,currency,created_at\r\n";
    static int _day;

    /// <summary>The in-memory database is shared by name across the hosts of one process: each test gets a far-future day of its own that no other test seeds.</summary>
    static DateOnly OwnDay() => new DateOnly(2200, 1, 1).AddDays(Interlocked.Increment(ref _day) * 400);

    static DateTimeOffset At(DateOnly d, int h = 0, int m = 0, int s = 0) => new(d.Year, d.Month, d.Day, h, m, s, TimeSpan.Zero);
    static string D(DateOnly d) => d.ToString("yyyy-MM-dd");

    static LedgerEntry Entry(Guid booking, LedgerKind kind, long amount, DateTimeOffset at, string? pair = null, Guid? id = null) =>
        new() { Id = id ?? Guid.NewGuid(), BookingId = booking, Kind = kind, AmountMinor = amount, PairKey = pair, CreatedAt = at };

    static Task<HttpResponseMessage> Get(CreateHost h, string query, string role = "Admin") =>
        h.ClientFor("demo-admin-1", role).GetAsync("/api/v1/admin/ledger.csv" + query);

    static async Task<string> Csv(CreateHost h, DateOnly from, DateOnly to)
    {
        var r = await Get(h, $"?from={D(from)}&to={D(to)}");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return await r.Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task Header_and_rows_match_the_contract_for_every_kind_in_order()
    {
        await using var h = new CreateHost();
        var day = OwnDay();
        var b1 = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
        var b2 = Guid.Parse("00000000-0000-0000-0000-0000000000a2");
        await h.Seed(db => db.LedgerEntries.AddRange(
            Entry(b2, LedgerKind.PayoutReversal, 1500, At(day, 18, 0, 0)),
            Entry(b1, LedgerKind.ClientCharge, 10000, At(day, 9, 30, 0)),
            Entry(b1, LedgerKind.Refund, 2500, At(day, 10, 0, 0)),
            Entry(b1, LedgerKind.PlatformFee, 1000, At(day, 11, 0, 0), pair: "demo-client-x:demo-prac-x"),
            Entry(b2, LedgerKind.PractitionerPayout, 7500, At(day, 12, 0, 5))));
        var expected = Header +
            "00000000-0000-0000-0000-0000000000a1,ClientCharge,10000,PLN,2200-01-01T09:30:00Z\r\n".Replace("2200-01-01", D(day)) +
            "00000000-0000-0000-0000-0000000000a1,Refund,2500,PLN,2200-01-01T10:00:00Z\r\n".Replace("2200-01-01", D(day)) +
            "00000000-0000-0000-0000-0000000000a1,PlatformFee,1000,PLN,2200-01-01T11:00:00Z\r\n".Replace("2200-01-01", D(day)) +
            "00000000-0000-0000-0000-0000000000a2,PractitionerPayout,7500,PLN,2200-01-01T12:00:05Z\r\n".Replace("2200-01-01", D(day)) +
            "00000000-0000-0000-0000-0000000000a2,PayoutReversal,1500,PLN,2200-01-01T18:00:00Z\r\n".Replace("2200-01-01", D(day));
        Assert.Equal(expected, await Csv(h, day, day));
    }

    [Fact]
    public async Task The_date_range_is_inclusive_in_UTC_days()
    {
        await using var h = new CreateHost();
        var from = OwnDay();
        var to = from.AddDays(1);
        var b = Guid.NewGuid();
        await h.Seed(db => db.LedgerEntries.AddRange(
            Entry(b, LedgerKind.ClientCharge, 1, At(from, 0, 0, 0).AddSeconds(-1)),   // the day before: out
            Entry(b, LedgerKind.ClientCharge, 2, At(from, 0, 0, 0)),                  // 00:00:00Z of from: in
            Entry(b, LedgerKind.ClientCharge, 3, At(to, 23, 59, 59)),                 // 23:59:59Z of to: in
            Entry(b, LedgerKind.ClientCharge, 4, At(to.AddDays(1), 0, 0, 0))));       // 00:00:00Z of the next day: out
        var lines = (await Csv(h, from, to)).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal([$"{b},ClientCharge,2,PLN,{D(from)}T00:00:00Z", $"{b},ClientCharge,3,PLN,{D(to)}T23:59:59Z"], lines[1..]);
    }

    [Fact]
    public async Task A_non_UTC_offset_instant_is_filtered_and_written_as_UTC()
    {
        await using var h = new CreateHost();
        var day = OwnDay();
        var b = Guid.NewGuid();
        // 00:30+02:00 on `day` is 22:30Z the day before: out of [day, day]; 01:30+02:00 the next day is 23:30Z of `day`: in.
        await h.Seed(db => db.LedgerEntries.AddRange(
            Entry(b, LedgerKind.Refund, 1, new DateTimeOffset(day.Year, day.Month, day.Day, 0, 30, 0, TimeSpan.FromHours(2))),
            Entry(b, LedgerKind.Refund, 2, new DateTimeOffset(day.AddDays(1).Year, day.AddDays(1).Month, day.AddDays(1).Day, 1, 30, 0, TimeSpan.FromHours(2)))));
        Assert.Equal(Header + $"{b},Refund,2,PLN,{D(day)}T23:30:00Z\r\n", await Csv(h, day, day));
    }

    [Fact]
    public async Task Equal_timestamps_are_ordered_by_entry_id_and_repeat_identically()
    {
        await using var h = new CreateHost();
        var day = OwnDay();
        var at = At(day, 12, 0, 0);
        var ids = Enumerable.Range(1, 6).Select(i => new Guid(i, 0, 0, [0, 0, 0, 0, 0, 0, 0, 0])).ToArray();
        var amounts = new Dictionary<Guid, long>();
        await h.Seed(db =>
        {
            // inserted in a scrambled order; the amount encodes the id order
            foreach (var i in new[] { 4, 1, 6, 3, 5, 2 })
            {
                amounts[ids[i - 1]] = i * 100;
                db.LedgerEntries.Add(Entry(Guid.NewGuid(), LedgerKind.ClientCharge, i * 100, at, id: ids[i - 1]));
            }
        });
        var first = await Csv(h, day, day);
        var amountsInOrder = first.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)[1..].Select(l => l.Split(',')[2]).ToArray();
        Assert.Equal(["100", "200", "300", "400", "500", "600"], amountsInOrder);
        Assert.Equal(first, await Csv(h, day, day));
    }

    [Fact]
    public async Task No_user_id_pair_key_or_entry_id_is_in_the_output()
    {
        await using var h = new CreateHost();
        var day = OwnDay();
        var entryId = Guid.NewGuid();
        var b = Guid.NewGuid();
        const string pair = "demo-client-secret-77:demo-practitioner-secret-88";
        await h.Seed(db => db.LedgerEntries.Add(Entry(b, LedgerKind.PlatformFee, 900, At(day, 8), pair, entryId)));
        var csv = await Csv(h, day, day);
        Assert.Contains(b.ToString(), csv);
        Assert.DoesNotContain(entryId.ToString(), csv);
        Assert.DoesNotContain("demo-client-secret-77", csv);
        Assert.DoesNotContain("demo-practitioner-secret-88", csv);
        Assert.DoesNotContain(pair, csv);
        Assert.Equal(Header + $"{b},PlatformFee,900,PLN,{D(day)}T08:00:00Z\r\n", csv);
    }

    [Fact]
    public async Task An_empty_range_returns_only_the_header()
    {
        await using var h = new CreateHost();
        var day = OwnDay();
        Assert.Equal(Header, await Csv(h, day, day));
    }

    [Fact]
    public async Task Content_type_and_disposition_are_set()
    {
        await using var h = new CreateHost();
        var day = OwnDay();
        var r = await Get(h, $"?from={D(day)}&to={D(day.AddDays(2))}");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("text/csv; charset=utf-8", r.Content.Headers.ContentType!.ToString());
        Assert.Equal($"attachment; filename=\"ledger-{D(day)}-{D(day.AddDays(2))}.csv\"", r.Content.Headers.ContentDisposition!.ToString());
        var bytes = await r.Content.ReadAsByteArrayAsync();
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "no BOM");
        Assert.Equal(Header, Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public async Task A_large_range_streams_completely_and_in_order_across_chunks()
    {
        await using var h = new CreateHost();
        var day = OwnDay();
        const int n = 2500;
        var start = At(day, 0, 0, 0);
        await h.Seed(db =>
        {
            for (var i = 0; i < n; i++)
                // 2500 rows; every timestamp is shared by three rows, so a tie straddles the page boundary at row 1000 (999, 1000 and 1001 share one)
                db.LedgerEntries.Add(Entry(Guid.NewGuid(), LedgerKind.ClientCharge, i + 1, start.AddSeconds(i / 3)));
        });
        var rows = (await Csv(h, day, day)).Split("\r\n", StringSplitOptions.RemoveEmptyEntries)[1..];
        Assert.Equal(n, rows.Length);
        Assert.Equal(Enumerable.Range(1, n).Select(i => (long)i).Order(), rows.Select(l => long.Parse(l.Split(',')[2])).Order()); // complete, no duplicates
        var times = rows.Select(l => l.Split(',')[4]).ToArray();
        Assert.Equal(times.Order(StringComparer.Ordinal), times);                                                                      // ordered by created_at
        Assert.Equal(n, rows.Distinct().Count());
    }

    [Fact]
    public async Task Only_the_range_is_read_not_other_days()
    {
        await using var h = new CreateHost();
        var day = OwnDay();
        await h.Seed(db =>
        {
            db.LedgerEntries.Add(Entry(Guid.NewGuid(), LedgerKind.Refund, 5, At(day.AddDays(40), 12)));
        });
        Assert.Equal(Header, await Csv(h, day, day.AddDays(30)));
    }

    [Fact]
    public async Task A_non_admin_gets_403_and_an_anonymous_caller_401()
    {
        await using var h = new CreateHost();
        var q = "?from=2030-01-01&to=2030-01-02";
        Assert.Equal(HttpStatusCode.Forbidden, (await h.ClientFor("demo-client-1").GetAsync("/api/v1/admin/ledger.csv" + q)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Get(h, q, role: "Practitioner")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.ClientFor(null).GetAsync("/api/v1/admin/ledger.csv" + q)).StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?from=2030-01-01")]
    [InlineData("?to=2030-01-01")]
    [InlineData("?from=2030-13-01&to=2030-12-01")]
    [InlineData("?from=2030-02-30&to=2030-03-01")]
    [InlineData("?from=abc&to=2030-01-01")]
    [InlineData("?from=2030-01-01&to=")]
    [InlineData("?from=2030-1-1&to=2030-01-02")]
    [InlineData("?from=2030-01-01T00:00:00Z&to=2030-01-02")]
    [InlineData("?from=2030-01-02&to=2030-01-01")]
    [InlineData("?from=2030-01-01&to=2031-01-02")] // 367 days inclusive
    public async Task Invalid_missing_reversed_or_too_long_dates_are_400_validation_failed_with_errors(string query)
    {
        await using var h = new CreateHost();
        var r = await Get(h, query);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        var body = await r.Body();
        Assert.Equal("validation_failed", body.GetProperty("code").GetString());
        Assert.True(body.GetProperty("errors").EnumerateObject().Any());
    }

    [Fact]
    public async Task A_range_of_exactly_366_days_is_accepted_and_a_single_day_too()
    {
        await using var h = new CreateHost();
        Assert.Equal(HttpStatusCode.OK, (await Get(h, "?from=2030-01-01&to=2031-01-01")).StatusCode); // 366 days inclusive
        Assert.Equal(HttpStatusCode.OK, (await Get(h, "?from=2030-01-01&to=2030-01-01")).StatusCode);
    }

    [Fact]
    public async Task Nothing_but_a_count_is_logged()
    {
        await using var h = new CreateHost();
        var day = OwnDay();
        var b = Guid.NewGuid();
        var entryId = Guid.NewGuid();
        await h.Seed(db => db.LedgerEntries.Add(Entry(b, LedgerKind.PlatformFee, 777, At(day, 8), "demo-pair-key-zz:demo-pract-zz", entryId)));
        await Csv(h, day, day);
        var logs = string.Join("\n", h.Logs.Lines);
        Assert.DoesNotContain(b.ToString(), logs);
        Assert.DoesNotContain(entryId.ToString(), logs);
        Assert.DoesNotContain("demo-pair-key-zz", logs);
    }
}
