using Gabinety.Booking.Features.Payouts;
using Gabinety.Booking.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Gabinety.Booking.Tests;

/// <summary>The payout job on real PostgreSQL 17: the unique index on <c>booking_id</c>, the row locks and the open-transaction check cannot be shown on InMemory.</summary>
[Trait("tier", "integration")]
public class PayoutIntegrationTests : IAsyncLifetime
{
    PostgreSqlContainer pg = null!;
    CreateHost host = null!;
    PayoutProvider provider = null!;

    public async Task InitializeAsync()
    {
        pg = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await pg.StartAsync();
        (host, provider) = PayJob.Host(("Database:Provider", "PostgreSQL"), ("ConnectionStrings:bookingdb", pg.GetConnectionString()));
        host.CreateClient();
        await host.Services.GetRequiredService<MigrationState>().WaitForCompletionAsync(TimeSpan.FromSeconds(60));
    }

    public async Task DisposeAsync() { await host.DisposeAsync(); await pg.DisposeAsync(); }

    BookingDbContext NewDb() => new(new DbContextOptionsBuilder<BookingDbContext>().UseNpgsql(pg.GetConnectionString()).Options);

    [Fact]
    public async Task Two_payout_jobs_at_once_pay_each_booking_exactly_once_for_every_interleaving()
    {
        for (var round = 0; round < 3; round++)
        {
            var ids = new List<Guid>();
            for (var i = 0; i < 10; i++) ids.Add(await PayJob.Seed(host, slot: round * 10 + i));
            var sentBefore = provider.Payouts.Count;
            var (a, b) = (PayJob.Job(host, provider), PayJob.Job(host, provider));
            var now = host.Clock.GetUtcNow();

            // No exception from either run (a lost insert race or a lost completion race is a skip, never an error); every payout is completed by exactly one of them.
            var paid = await Task.WhenAll(Task.Run(() => a.PayDueAsync(now, default)), Task.Run(() => b.PayDueAsync(now, default)));

            Assert.Equal(ids.Count, paid.Sum());
            await using var db = NewDb();
            foreach (var id in ids)
            {
                Assert.Equal(PayoutStatus.Sent, (await db.Payouts.AsNoTracking().SingleAsync(p => p.BookingId == id)).Status); // one row: the unique index on booking_id
                var ledger = await db.LedgerEntries.AsNoTracking().Where(l => l.BookingId == id && l.Kind == Gabinety.Contracts.Booking.LedgerKind.PractitionerPayout).ToListAsync();
                Assert.Equal(8500, Assert.Single(ledger).AmountMinor);
            }
            var calls = provider.Payouts.Skip(sentBefore).Where(r => ids.Select(PayJob.Key).Contains(r.IdempotencyKey)).ToList();
            // the provider may see a key more than once on a lost race (it is idempotent on the key), never a second key for one booking
            Assert.Equal(ids.Count, calls.Select(r => r.IdempotencyKey).Distinct().Count());
        }
    }

    [Fact]
    public async Task The_provider_call_runs_with_no_transaction_open_in_the_database()
    {
        await PayJob.Seed(host);
        var openDuringCall = new List<long>();
        provider.Before = async _ =>
        {
            await using var probe = NewDb();
            openDuringCall.Add(await probe.Database.SqlQueryRaw<long>(
                "SELECT count(*) AS \"Value\" FROM pg_stat_activity WHERE datname = current_database() AND pid <> pg_backend_pid() AND state LIKE 'idle in transaction%'").SingleAsync());
        };

        Assert.Equal(1, await PayJob.Job(host, provider).PayDueAsync(host.Clock.GetUtcNow(), default));

        Assert.Equal([0L], openDuringCall); // asked once, from outside, while the provider was being called
    }
}
