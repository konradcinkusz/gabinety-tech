using Gabinety.Booking.Features.Bookings.Create;
using Gabinety.Booking.Features.Payouts;
using Gabinety.Booking.Persistence;
using Gabinety.Contracts.Booking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;
using BookingStatus = Gabinety.Billing.BookingStatus;
using BookingTrigger = Gabinety.Billing.BookingTrigger;

namespace Gabinety.Booking.Tests;

/// <summary>The pair rule and the payouts table on real PostgreSQL 17 (CONTRACTS 4.1, 9.1).</summary>
[Trait("tier", "integration")]
public class FeeIntegrationTests : IAsyncLifetime
{
    PostgreSqlContainer pg = null!;
    string cs = "";
    CreateHost host = null!;

    public async Task InitializeAsync()
    {
        pg = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await pg.StartAsync();
        cs = pg.GetConnectionString();
        host = Life.Host(new RecordingProvider(), h =>
        {
            h.Settings["Database:Provider"] = "PostgreSQL";
            h.Settings["ConnectionStrings:bookingdb"] = cs;
        });
        host.CreateClient();
        await host.Services.GetRequiredService<MigrationState>().WaitForCompletionAsync(TimeSpan.FromSeconds(60));
    }

    public async Task DisposeAsync() { await host.DisposeAsync(); await pg.DisposeAsync(); }

    BookingDbContext Db() => new(new DbContextOptionsBuilder<BookingDbContext>().UseNpgsql(cs).Options);

    [Fact]
    public async Task Two_concurrent_completions_of_two_first_bookings_of_one_pair_write_one_fee_row()
    {
        for (var round = 0; round < 10; round++)
        {
            // A new pair every round; the two bookings are 1 and 2 days ahead (inside the 60-day horizon, no overlap for the practitioner).
            var client = Fee.NewClient();
            var practitioner = Guid.NewGuid();
            var a = await Fee.Seed(host, BookingStatus.Confirmed, client, practitioner, 25000, TimeSpan.FromDays(1));
            var b = await Fee.Seed(host, BookingStatus.Confirmed, client, practitioner, 25000, TimeSpan.FromDays(2));

            var results = await Task.WhenAll(new[] { a, b }.Select(id => Task.Run(() => Fee.Apply(host, id, BookingTrigger.CompletionWindowElapsed))));
            Assert.All(results, r => Assert.True(r.Applied, $"round {round}"));

            await using var db = Db();
            var rows = await db.LedgerEntries.Where(l => l.Kind == LedgerKind.PlatformFee && l.PairKey == $"{client}:{practitioner}").ToListAsync();
            Assert.Single(rows);
            var fees = await db.Bookings.Where(x => x.Id == a || x.Id == b).Select(x => new { x.Id, x.FeeMinor, x.PayoutMinor }).ToListAsync();
            Assert.Equal(new long?[] { 0, 3750 }, fees.Select(f => f.FeeMinor).Order().ToArray());
            Assert.Equal(new long?[] { 21250, 25000 }, fees.Select(f => f.PayoutMinor).Order().ToArray());
            Assert.Equal(rows[0].BookingId, fees.Single(f => f.FeeMinor == 3750).Id);
        }
    }

    [Fact]
    public async Task A_second_booking_of_a_pair_gets_fee_zero_through_ON_CONFLICT_DO_NOTHING()
    {
        // The fee row is taken: the second insert hits ON CONFLICT DO NOTHING (0 rows), the transition still commits and the fee is 0.
        var client = Fee.NewClient();
        var practitioner = Guid.NewGuid();
        var a = await Fee.Seed(host, BookingStatus.Confirmed, client, practitioner, 10000, TimeSpan.FromDays(1));
        var b = await Fee.Seed(host, BookingStatus.Confirmed, client, practitioner, 10000, TimeSpan.FromDays(2));
        await Fee.Apply(host, a, BookingTrigger.CompletionWindowElapsed);
        await Fee.Apply(host, b, BookingTrigger.CompletionWindowElapsed);
        Assert.Equal((1500L, 0L), ((await Life.Row(host, a)).FeeMinor, (await Life.Row(host, b)).FeeMinor));
        Assert.Single(await Fee.FeeRows(host, client, practitioner));
    }

    [Fact]
    public async Task A_64_character_client_id_does_not_overflow_pair_key_and_still_gives_one_fee_per_pair()
    {
        var client = "demo-" + Guid.NewGuid().ToString("N") + new string('z', 27); // 64 characters: the spec format would be 101
        var practitioner = Guid.NewGuid();
        var a = await Fee.Seed(host, BookingStatus.Confirmed, client, practitioner, 25000, TimeSpan.FromDays(1));
        var b = await Fee.Seed(host, BookingStatus.Confirmed, client, practitioner, 25000, TimeSpan.FromDays(2));
        var results = await Task.WhenAll(new[] { a, b }.Select(id => Task.Run(() => Fee.Apply(host, id, BookingTrigger.CompletionWindowElapsed))));
        Assert.All(results, r => Assert.True(r.Applied));

        await using var db = Db();
        var row = Assert.Single(await db.LedgerEntries.Where(l => l.Kind == LedgerKind.PlatformFee && l.PairKey == PairKey.For(client, practitioner)).ToListAsync());
        Assert.StartsWith("h:", row.PairKey);
        Assert.Equal(new long?[] { 0, 3750 }, (await db.Bookings.Where(x => x.Id == a || x.Id == b).Select(x => x.FeeMinor).ToListAsync()).Order().ToArray());
    }

    [Fact]
    public async Task The_payouts_table_exists_and_booking_id_is_unique()
    {
        await using var db = Db();
        var booking = Guid.NewGuid();
        db.Payouts.Add(new Payout { Id = Guid.NewGuid(), BookingId = booking, PractitionerId = Guid.NewGuid(), AmountMinor = 21250, Status = PayoutStatus.Pending, CreatedAt = DateTimeOffset.UnixEpoch });
        await db.SaveChangesAsync();

        await using var db2 = Db();
        db2.Payouts.Add(new Payout { Id = Guid.NewGuid(), BookingId = booking, PractitionerId = Guid.NewGuid(), AmountMinor = 1, Status = PayoutStatus.Pending, CreatedAt = DateTimeOffset.UnixEpoch });
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db2.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(ex.InnerException).SqlState);

        await using var check = Db();
        Assert.Equal("Pending", (await check.Database.SqlQuery<string>($"select status as \"Value\" from payouts where booking_id = {booking}").ToListAsync()).Single());
    }
}
