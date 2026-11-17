using Gabinety.Booking.Jobs.Base;
using Gabinety.Booking.Jobs.HoldExpiry;
using Gabinety.Booking.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;
using BookingStatus = Gabinety.Billing.BookingStatus;

namespace Gabinety.Booking.Tests;

/// <summary>Two job instances at once on real PostgreSQL 17: the row lock in the transition makes each hold expire exactly once.</summary>
[Trait("tier", "integration")]
public class HoldExpiryIntegrationTests : IAsyncLifetime
{
    PostgreSqlContainer pg = null!;
    CreateHost host = null!;

    public async Task InitializeAsync()
    {
        pg = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await pg.StartAsync();
        host = Hold.Host(null, ("Database:Provider", "PostgreSQL"), ("ConnectionStrings:bookingdb", pg.GetConnectionString()));
        host.CreateClient();
        await host.Services.GetRequiredService<MigrationState>().WaitForCompletionAsync(TimeSpan.FromSeconds(60));
    }

    public async Task DisposeAsync() { await host.DisposeAsync(); await pg.DisposeAsync(); }

    HoldExpiryJob NewJob() =>
        new(host.Services.GetRequiredService<IServiceScopeFactory>(), host.Clock, host.Services.GetRequiredService<IConfiguration>(),
            host.Services.GetRequiredService<JobMetrics>(), host.Services.GetRequiredService<ILogger<HoldExpiryJob>>());

    [Fact]
    public async Task Two_jobs_running_at_once_expire_each_due_hold_exactly_once_without_an_error()
    {
        for (var round = 0; round < 5; round++)
        {
            var ids = new List<Guid>();
            for (var i = 0; i < 12; i++) ids.Add(await Hold.Seed(host, TimeSpan.FromMinutes(1), slot: round * 12 + i));
            host.Clock.Advance(TimeSpan.FromMinutes(5));
            var now = host.Clock.GetUtcNow();
            var (a, b) = (NewJob(), NewJob());
            var before = host.Logs.Lines.Count(l => l.Contains("PendingPayment -> Expired", StringComparison.Ordinal));

            var applied = await Task.WhenAll(Task.Run(() => a.ExpireDueAsync(now, default)), Task.Run(() => b.ExpireDueAsync(now, default)));

            // For every interleaving: no exception, every hold is Expired, and the two runs' applied counts add up to the number of holds (no double transition).
            Assert.Equal(ids.Count, applied.Sum());
            await using var db = new BookingDbContext(new DbContextOptionsBuilder<BookingDbContext>().UseNpgsql(pg.GetConnectionString()).Options);
            Assert.All(await db.Bookings.Where(x => ids.Contains(x.Id)).Select(x => x.Status).ToListAsync(), s => Assert.Equal(BookingStatus.Expired, s));
            var moved = host.Logs.Lines.Count(l => l.Contains("PendingPayment -> Expired", StringComparison.Ordinal)) - before;
            Assert.Equal(ids.Count, moved);
            foreach (var id in ids) Assert.Equal(1, host.Logs.Lines.Count(l => l.Contains(id.ToString()) && l.Contains("PendingPayment -> Expired", StringComparison.Ordinal)));
        }
    }
}
