using System.Net;
using Gabinety.Booking.Persistence;
using Gabinety.Contracts.Booking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;
using BookingStatus = Gabinety.Billing.BookingStatus;

namespace Gabinety.Booking.Tests;

/// <summary>The webhook on real PostgreSQL 17: the event key, the row lock, the migration and the partial unique index on pair_key.</summary>
[Trait("tier", "integration")]
public class PaymentWebhookIntegrationTests : IAsyncLifetime
{
    PostgreSqlContainer pg = null!;
    string cs = "";
    CreateHost host = null!;

    public async Task InitializeAsync()
    {
        pg = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await pg.StartAsync();
        cs = pg.GetConnectionString();
        host = new CreateHost { MeetingUrl = "https://meet.demo.invalid/room-1" };
        host.Settings["Database:Provider"] = "PostgreSQL";
        host.Settings["ConnectionStrings:bookingdb"] = cs;
        host.Settings["Payments:WebhookSecret"] = Pay.Secret;
        host.CreateClient();
        await host.Services.GetRequiredService<MigrationState>().WaitForCompletionAsync(TimeSpan.FromSeconds(60));
        await host.WorkWeek();
    }

    public async Task DisposeAsync() { await host.DisposeAsync(); await pg.DisposeAsync(); }

    BookingDbContext Db() => new(new DbContextOptionsBuilder<BookingDbContext>().UseNpgsql(cs).Options);

    /// <summary>10:00 UTC of the n-th weekday after the clock's Monday (2026-11-16).</summary>
    static string Day(int n)
    {
        var d = new DateTime(2026, 11, 16, 10, 0, 0, DateTimeKind.Utc);
        for (var left = n + 1; left > 0;) { d = d.AddDays(1); if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) left--; }
        return d.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
    }

    static async Task<string> Outcome(HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, r.StatusCode + " " + await r.Content.ReadAsStringAsync());
        return (await r.Body()).GetProperty("outcome").GetString()!;
    }

    [Fact]
    public async Task The_same_event_delivered_twice_at_once_is_applied_once_and_the_other_answer_is_Duplicate()
    {
        var c = host.ClientFor(null);
        for (var round = 0; round < 10; round++)
        {
            var (id, pid) = await Pay.CreateBooking(host, "demo-client-" + round, Day(round));
            var body = Pay.EventJson($"evt_race_{round}", "PaymentSucceeded", pid);
            var sig = Pay.Sign(body);
            var rs = await Task.WhenAll(Task.Run(() => Pay.Webhook(c, body, sig)), Task.Run(() => Pay.Webhook(c, body, sig)));
            var outcomes = (await Task.WhenAll(rs.Select(Outcome))).Order().ToArray();
            Assert.True(outcomes.SequenceEqual(["Applied", "Duplicate"]), $"round {round}: {string.Join(",", outcomes)}");
            await using var db = Db();
            Assert.Equal(1, await db.LedgerEntries.CountAsync(l => l.BookingId == id));
            Assert.Equal(1, await db.WebhookEvents.CountAsync(x => x.EventId == $"evt_race_{round}"));
            Assert.Equal(BookingStatus.Confirmed, (await db.Bookings.SingleAsync(b => b.Id == id)).Status);
        }
    }

    [Fact]
    public async Task A_success_and_a_failure_for_one_booking_at_once_apply_exactly_one_and_leave_a_consistent_state()
    {
        var c = host.ClientFor(null);
        for (var round = 0; round < 10; round++)
        {
            var (id, pid) = await Pay.CreateBooking(host, "demo-client-x" + round, Day(round + 12));
            var ok = Pay.EventJson($"evt_ok_{round}", "PaymentSucceeded", pid);
            var bad = Pay.EventJson($"evt_bad_{round}", "PaymentFailed", pid, null);
            var rs = await Task.WhenAll(Task.Run(() => Pay.Webhook(c, ok, Pay.Sign(ok))), Task.Run(() => Pay.Webhook(c, bad, Pay.Sign(bad))));
            var outcomes = (await Task.WhenAll(rs.Select(Outcome))).Order().ToArray();
            Assert.True(outcomes.SequenceEqual(["Applied", "Ignored"]), $"round {round}: {string.Join(",", outcomes)}");
            await using var db = Db();
            var booking = await db.Bookings.SingleAsync(b => b.Id == id);
            var charges = await db.LedgerEntries.CountAsync(l => l.BookingId == id && l.Kind == LedgerKind.ClientCharge);
            Assert.Equal(booking.Status == BookingStatus.Confirmed ? 1 : 0, charges);
            Assert.Contains(booking.Status, new[] { BookingStatus.Confirmed, BookingStatus.Expired });
        }
    }

    [Fact]
    public async Task A_late_success_is_refunded_and_recorded_on_PostgreSQL()
    {
        var (id, pid) = await Pay.CreateBooking(host, "demo-client-late", Day(30));
        host.Clock.Advance(TimeSpan.FromMinutes(11));
        var body = Pay.EventJson("evt_late_1", "PaymentSucceeded", pid);
        Assert.Equal("Applied", await Outcome(await Pay.Webhook(host.ClientFor(null), body, Pay.Sign(body))));
        await using var db = Db();
        Assert.Equal(BookingStatus.Expired, (await db.Bookings.SingleAsync(b => b.Id == id)).Status);
        Assert.Equal([LedgerKind.ClientCharge, LedgerKind.Refund], (await db.LedgerEntries.Where(l => l.BookingId == id).ToListAsync()).Select(l => l.Kind).Order().ToArray());
        var payment = await db.Payments.SingleAsync(p => p.BookingId == id);
        Assert.Equal((PaymentStatus.Refunded, 10000L), (payment.Status, payment.RefundedMinor));
    }

    [Fact]
    public async Task The_migration_applies_and_a_second_PlatformFee_for_one_pair_is_refused_by_the_partial_unique_index()
    {
        await using var c = new NpgsqlConnection(cs);
        await c.OpenAsync();
        async Task<List<string>> Rows(string sql)
        {
            await using var cmd = new NpgsqlCommand(sql, c);
            await using var r = await cmd.ExecuteReaderAsync();
            var rows = new List<string>();
            while (await r.ReadAsync()) rows.Add(r.GetString(0));
            return rows;
        }

        var declared = Db().Database.GetMigrations().ToList();
        Assert.Contains("20261117090000_T-04.06_Webhook", declared);
        Assert.Equal(declared, await Rows("""select "MigrationId" from "__EFMigrationsHistory" order by 1"""));
        Assert.Equal(["provider", "event_id"], await Rows(
            "select a.attname from pg_index i join pg_attribute a on a.attrelid = i.indrelid and a.attnum = any(i.indkey) " +
            "where i.indrelid = 'webhook_events'::regclass and i.indisprimary order by array_position(i.indkey::int2[], a.attnum)"));
        var index = Assert.Single(await Rows("select indexdef from pg_indexes where tablename = 'ledger_entries' and indexdef like 'CREATE UNIQUE%' and indexdef like '%pair_key%'"));
        Assert.Contains("pair_key", index);
        Assert.Contains("PlatformFee", index);

        await using var db = Db();
        LedgerEntryOf(db, LedgerKind.PlatformFee, "demo-a:demo-p");
        await db.SaveChangesAsync();
        LedgerEntryOf(db, LedgerKind.ClientCharge, null); LedgerEntryOf(db, LedgerKind.ClientCharge, null); // not covered by the index
        await db.SaveChangesAsync();
        LedgerEntryOf(db, LedgerKind.PlatformFee, "demo-a:demo-p");
        var e = await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal("23505", ((PostgresException)e.InnerException!).SqlState);
    }

    static void LedgerEntryOf(BookingDbContext db, LedgerKind kind, string? pair) =>
        db.LedgerEntries.Add(new() { Id = Guid.NewGuid(), BookingId = Guid.NewGuid(), Kind = kind, AmountMinor = 1, PairKey = pair, CreatedAt = DateTimeOffset.UnixEpoch });
}
