using Gabinety.Billing;
using Gabinety.Booking.Features.Slots;
using Gabinety.Booking.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Gabinety.Booking.Tests;

/// <summary>The no-double-booking rule on real PostgreSQL 17 (CONTRACTS 4.1): the InMemory provider cannot enforce an exclusion constraint.</summary>
[Trait("tier", "integration")]
public class SlotsConstraintIntegrationTests : IAsyncLifetime
{
    static readonly DateTimeOffset Base = new(2027, 3, 1, 8, 0, 0, TimeSpan.Zero);
    PostgreSqlContainer pg = null!;
    string cs = "";

    public async Task InitializeAsync()
    {
        pg = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await pg.StartAsync();
        cs = pg.GetConnectionString();
        await using var db = Db();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await pg.DisposeAsync();

    BookingDbContext Db() => new(new DbContextOptionsBuilder<BookingDbContext>().UseNpgsql(cs).Options);

    static async Task Insert(BookingDbContext db, Guid practitioner, DateTimeOffset start, int minutes = 60, BookingStatus status = BookingStatus.Confirmed)
    {
        db.Bookings.Add(Slots.NewBooking(practitioner, start, minutes, status));
        await BookingSlotGuard.SaveAsync(db, default);
    }

    static async Task<bool> TryInsert(string cs, Guid practitioner, DateTimeOffset start, int minutes = 60, BookingStatus status = BookingStatus.Confirmed)
    {
        await using var db = new BookingDbContext(new DbContextOptionsBuilder<BookingDbContext>().UseNpgsql(cs).Options);
        try { await Insert(db, practitioner, start, minutes, status); return true; }
        catch (SlotTakenException) { return false; }
    }

    [Fact]
    public async Task Two_concurrent_inserts_for_overlapping_intervals_one_succeeds_and_the_other_is_slot_taken()
    {
        for (var round = 0; round < 10; round++)
        {
            var practitioner = Guid.NewGuid();
            var start = Base.AddDays(round);
            using var barrier = new Barrier(2);
            async Task<Exception?> Racer(DateTimeOffset s)
            {
                await using var db = Db();
                db.Bookings.Add(Slots.NewBooking(practitioner, s, 60, BookingStatus.PendingPayment, Base));
                await db.Database.OpenConnectionAsync(); // both sides are connected and ready before either writes
                barrier.SignalAndWait();
                try { await BookingSlotGuard.SaveAsync(db, default); return null; }
                catch (Exception e) { return e; }
            }

            var results = await Task.WhenAll(Task.Run(() => Racer(start)), Task.Run(() => Racer(start.AddMinutes(30))));
            // Say WHAT happened when it goes wrong: the types and the inner exceptions of both racers.
            var what = $"round {round}: " + string.Join(" | ", results.Select(r => r is null ? "ok"
                : $"{r.GetType().Name}: {r.Message.Split('\n')[0]} <- {r.InnerException?.GetType().Name}: {r.InnerException?.Message.Split('\n')[0]}"));
            Assert.True(results.Count(r => r is null) == 1, what);
            Assert.True(results.Count(r => r is SlotTakenException) == 1, what);
            await using var check = Db();
            Assert.Equal(1, await check.Bookings.CountAsync(b => b.PractitionerId == practitioner));
        }
    }

    [Fact]
    public async Task Bookings_whose_buffer_ends_exactly_at_the_next_start_both_succeed()
    {
        var p = Guid.NewGuid();
        Assert.True(await TryInsert(cs, p, Base));                          // 08:00-09:00, buffer to 09:15
        Assert.True(await TryInsert(cs, p, Base.AddMinutes(75)));           // starts at 09:15
    }

    [Fact]
    public async Task A_booking_14_minutes_after_the_end_conflicts()
    {
        var p = Guid.NewGuid();
        Assert.True(await TryInsert(cs, p, Base));
        Assert.False(await TryInsert(cs, p, Base.AddMinutes(60 + 14)));
    }

    [Fact]
    public async Task A_booking_ending_inside_the_buffer_of_a_later_one_conflicts_in_the_other_direction()
    {
        var p = Guid.NewGuid();
        Assert.True(await TryInsert(cs, p, Base.AddMinutes(75)));           // 09:15-10:15
        Assert.False(await TryInsert(cs, p, Base, 61));                     // 08:00-09:01 + 15 = 09:16 > 09:15
        Assert.True(await TryInsert(cs, p, Base, 60));                      // 08:00-09:00 + 15 = 09:15: adjacent
    }

    [Theory]
    [InlineData(BookingStatus.CancelledByClient)]
    [InlineData(BookingStatus.Expired)]
    [InlineData(BookingStatus.Completed)]
    [InlineData(BookingStatus.CancelledByPractitioner)]
    [InlineData(BookingStatus.LateCancelledByClient)]
    [InlineData(BookingStatus.ClientNoShow)]
    [InlineData(BookingStatus.Resolved)]
    public async Task An_inactive_booking_does_not_block_a_new_overlapping_one(BookingStatus inactive)
    {
        var p = Guid.NewGuid();
        Assert.True(await TryInsert(cs, p, Base, status: inactive));
        Assert.True(await TryInsert(cs, p, Base.AddMinutes(10)));
    }

    [Theory]
    [InlineData(BookingStatus.PendingPayment)]
    [InlineData(BookingStatus.Confirmed)]
    [InlineData(BookingStatus.Disputed)]
    public async Task An_active_booking_blocks(BookingStatus active)
    {
        var p = Guid.NewGuid();
        Assert.True(await TryInsert(cs, p, Base, status: active));
        Assert.False(await TryInsert(cs, p, Base.AddMinutes(10)));
    }

    [Fact]
    public async Task Cancelling_a_booking_frees_its_slot()
    {
        var p = Guid.NewGuid();
        await using (var db = Db()) await Insert(db, p, Base);
        Assert.False(await TryInsert(cs, p, Base));
        await using (var db = Db())
        {
            var b = await db.Bookings.SingleAsync(x => x.PractitionerId == p);
            b.Status = BookingStatus.CancelledByClient;
            await db.SaveChangesAsync();
        }
        Assert.True(await TryInsert(cs, p, Base));
    }

    [Fact]
    public async Task Two_different_practitioners_may_hold_the_same_interval()
    {
        Assert.True(await TryInsert(cs, Guid.NewGuid(), Base));
        Assert.True(await TryInsert(cs, Guid.NewGuid(), Base));
    }

    [Fact]
    public async Task An_expired_PendingPayment_hold_still_blocks_in_the_database_until_it_is_expired_by_ExpireStaleHoldsAsync()
    {
        var p = Guid.NewGuid();
        await using (var db = Db())
        {
            db.Bookings.Add(Slots.NewBooking(p, Base, 60, BookingStatus.PendingPayment, Base.AddDays(-1)));
            await db.SaveChangesAsync();
        }
        Assert.False(await TryInsert(cs, p, Base)); // the constraint cannot use now()

        await using var tx = Db();
        await using var transaction = await tx.Database.BeginTransactionAsync();
        await BookingSlotGuard.LockPractitionerAsync(tx, p, default);
        await BookingSlotGuard.ExpireStaleHoldsAsync(tx, p, Base, default);
        await tx.SaveChangesAsync(); // the stale row first, then the new one
        await Insert(tx, p, Base);
        await transaction.CommitAsync();
    }

    [Fact]
    public async Task The_advisory_lock_serialises_two_creators_of_one_practitioner()
    {
        var p = Guid.NewGuid();
        await using var a = Db();
        await using var ta = await a.Database.BeginTransactionAsync();
        await BookingSlotGuard.LockPractitionerAsync(a, p, default);

        await using var b = Db();
        await using var tb = await b.Database.BeginTransactionAsync();
        var second = Task.Run(() => BookingSlotGuard.LockPractitionerAsync(b, p, default));
        await Task.Delay(500);
        Assert.False(second.IsCompleted); // waits for the first transaction

        await ta.CommitAsync();
        await second.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task The_migration_creates_the_table_the_constraint_and_the_indexes_and_the_history_lists_every_migration()
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
        Assert.Equal(declared, await Rows("""select "MigrationId" from "__EFMigrationsHistory" order by 1"""));
        Assert.Contains("20261116130000_T-04.04_Bookings", declared);
        Assert.Contains("bookings_no_overlap", await Rows("select conname from pg_constraint where contype = 'x'"));
        Assert.Contains("btree_gist", await Rows("select extname from pg_extension"));
        Assert.Contains("IX_bookings_client_user_id_idempotency_key", await Rows("select indexname from pg_indexes where tablename = 'bookings'"));
        Assert.Contains("timestamp with time zone", await Rows("select data_type from information_schema.columns where table_name='bookings' and column_name='start_utc'"));
        Assert.Contains("character varying", await Rows("select data_type from information_schema.columns where table_name='bookings' and column_name='status'"));
    }

    [Fact]
    public async Task The_slots_endpoint_runs_against_real_PostgreSQL_and_a_booked_slot_disappears()
    {
        var practitioner = Guid.NewGuid();
        var offering = Guid.NewGuid();
        await using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("GABINETY_FAKE", "1"); // Mock payments need the fake profile (F-01)
            b.UseSetting("Database:Provider", "PostgreSQL");
            b.UseSetting("ConnectionStrings:bookingdb", cs);
            b.ConfigureServices(s =>
            {
                s.RemoveAll<Gabinety.Booking.Clients.IDirectoryClient>();
                s.AddSingleton<Gabinety.Booking.Clients.IDirectoryClient>(new StubOffering(practitioner, offering));
                s.RemoveAll<TimeProvider>();
                s.AddSingleton<TimeProvider>(new ManualClock());
            });
        });
        var client = host.CreateClient();
        await host.Services.GetRequiredService<MigrationState>().WaitForCompletionAsync(TimeSpan.FromSeconds(60));
        await using (var db = Db())
        {
            db.Rules.Add(new() { Id = Guid.NewGuid(), PractitionerId = practitioner, IsoWeekday = 2, StartLocal = new(9, 0), EndLocal = new(17, 0) });
            await db.SaveChangesAsync();
            await Insert(db, practitioner, new(2026, 11, 17, 10, 0, 0, TimeSpan.Zero));
        }
        var json = await client.GetStringAsync($"/api/v1/practitioners/{practitioner}/slots?offeringId={offering}&from=2026-11-17&to=2026-11-17");
        Assert.DoesNotContain("2026-11-17T10:00:00Z", json);
        Assert.Contains("2026-11-17T08:00:00Z", json);
        Assert.Contains("2026-11-17T11:30:00Z", json);
    }

    sealed class StubOffering(Guid practitioner, Guid offering) : Gabinety.Booking.Clients.IDirectoryClient
    {
        public Task<Gabinety.Booking.Clients.DirectoryOffering?> GetOfferingAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<Gabinety.Booking.Clients.DirectoryOffering?>(id != offering ? null : new(offering, practitioner, "demo-user-1",
                Gabinety.Contracts.Directory.PractitionerStatus.Approved, "demo-offering", 60, new(10000), null, true));
        public Task<Gabinety.Booking.Clients.DirectoryPractitioner?> GetPractitionerByUserAsync(string userId, CancellationToken ct) => Task.FromResult<Gabinety.Booking.Clients.DirectoryPractitioner?>(null);
        public Task<Gabinety.Booking.Clients.DirectoryPractitionerName?> GetPractitionerNameAsync(Guid id, CancellationToken ct) => Task.FromResult<Gabinety.Booking.Clients.DirectoryPractitionerName?>(null);
        public Task<Gabinety.Booking.Clients.DirectoryContact?> GetPractitionerContactAsync(string userId, CancellationToken ct) => Task.FromResult<Gabinety.Booking.Clients.DirectoryContact?>(null);
        public Task<bool> IsAliveAsync(CancellationToken ct) => Task.FromResult(true);
    }
}
