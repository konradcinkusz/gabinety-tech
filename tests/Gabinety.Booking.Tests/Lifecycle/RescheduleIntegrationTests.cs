using System.Net;
using Gabinety.Booking.Notifications;
using Gabinety.Booking.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;
using BookingStatus = Gabinety.Billing.BookingStatus;

namespace Gabinety.Booking.Tests;

/// <summary>T-11.10 on real PostgreSQL 17: the migration, and the practitioner lock plus the booking row lock under concurrent reschedules.</summary>
[Trait("tier", "integration")]
public class RescheduleIntegrationTests : IAsyncLifetime
{
    const int Rounds = 5;
    PostgreSqlContainer pg = null!;
    string cs = "";
    CreateHost host = null!;
    readonly RecordingProvider provider = new();

    public async Task InitializeAsync()
    {
        pg = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await pg.StartAsync();
        cs = pg.GetConnectionString();
        host = Life.Host(provider, h =>
        {
            h.Settings["Database:Provider"] = "PostgreSQL";
            h.Settings["ConnectionStrings:bookingdb"] = cs;
        });
        host.CreateClient();
        await host.Services.GetRequiredService<MigrationState>().WaitForCompletionAsync(TimeSpan.FromSeconds(60));
        await host.WorkWeek();
    }

    public async Task DisposeAsync() { await host.DisposeAsync(); await pg.DisposeAsync(); }

    BookingDbContext Db() => new(new DbContextOptionsBuilder<BookingDbContext>().UseNpgsql(cs).Options);

    /// <summary>The n-th weekday from Wednesday 2026-11-18, 10:00 UTC (11:00 Warsaw): on the practitioner's grid, 46 h or more after the host clock.</summary>
    static string Target(int n)
    {
        var d = new DateTime(2026, 11, 18, 10, 0, 0, DateTimeKind.Utc);
        for (var left = n; left > 0; left--)
        {
            d = d.AddDays(1);
            while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) d = d.AddDays(1);
        }
        return d.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
    }

    [Fact]
    public async Task The_migration_adds_reschedule_count_not_null_default_0_and_a_nullable_rescheduled_at()
    {
        await using var c = new NpgsqlConnection(cs);
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            select column_name, data_type, is_nullable, coalesce(column_default, '') from information_schema.columns
            where table_name = 'bookings' and column_name in ('reschedule_count', 'rescheduled_at') order by column_name
            """, c);
        await using var r = await cmd.ExecuteReaderAsync();
        var cols = new List<(string, string, string, string)>();
        while (await r.ReadAsync()) cols.Add((r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3)));

        Assert.Equal([("reschedule_count", "smallint", "NO", "0"), ("rescheduled_at", "timestamp with time zone", "YES", "")], cols);
    }

    [Fact]
    public async Task Two_bookings_of_one_practitioner_moved_into_one_slot_at_once_exactly_one_succeeds_and_the_other_is_409_slot_taken()
    {
        for (var round = 0; round < Rounds; round++)
        {
            // Seeded far enough out not to overlap each other, any other round or the targets (10:00 UTC for an hour; the seeds start at 12:00 UTC).
            var a = await Life.Seed(host, BookingStatus.Confirmed, TimeSpan.FromDays(30 + 2 * round));
            var b = await Life.Seed(host, BookingStatus.Confirmed, TimeSpan.FromDays(31 + 2 * round));
            var target = Target(round);

            var rs = await Task.WhenAll(Task.Run(() => Resched.Move(host, a, Life.Client, target)), Task.Run(() => Resched.Move(host, b, Life.Client, target)));

            var codes = rs.Select(r => r.StatusCode).Order().ToArray();
            Assert.True(codes.SequenceEqual([HttpStatusCode.OK, HttpStatusCode.Conflict]), $"round {round} ({target}): {string.Join(",", codes)}");
            Assert.Equal("slot_taken", await Resched.Code(rs.Single(r => r.StatusCode == HttpStatusCode.Conflict)));
            await using var db = Db();
            Assert.Equal(1, await db.Bookings.CountAsync(x => x.PractitionerId == host.Practitioner && x.StartUtc == DateTimeOffset.Parse(target)));
            var moved = Assert.Single(await db.Bookings.Where(x => (x.Id == a || x.Id == b) && x.RescheduleCount == 1).ToListAsync());
            var rescheduled = await db.Notifications.Where(n => (n.BookingId == a || n.BookingId == b) && n.Kind == NotificationKind.Rescheduled).ToListAsync();
            Assert.Equal(2, rescheduled.Count);
            Assert.All(rescheduled, n => Assert.Equal(moved.Id, n.BookingId));
        }
    }

    [Fact]
    public async Task Two_reschedules_of_one_booking_at_once_give_one_200_and_one_409_invalid_transition()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var id = await Life.Seed(host, BookingStatus.Confirmed, TimeSpan.FromDays(50 + round));
            var rs = await Task.WhenAll(Task.Run(() => Resched.Move(host, id, Life.Client, Target(10 + round))), Task.Run(() => Resched.Move(host, id, Life.Client, Target(20 + round))));

            var codes = rs.Select(r => r.StatusCode).Order().ToArray();
            Assert.True(codes.SequenceEqual([HttpStatusCode.OK, HttpStatusCode.Conflict]), $"round {round}: {string.Join(",", codes)}");
            Assert.Equal("invalid_transition", await Resched.Code(rs.Single(r => r.StatusCode == HttpStatusCode.Conflict)));
            await using var db = Db();
            Assert.Equal((short)1, (await db.Bookings.SingleAsync(x => x.Id == id)).RescheduleCount);
        }
    }
}
