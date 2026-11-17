using System.Net;
using Gabinety.Contracts.Booking;
using Gabinety.Booking.Features.Payments;
using Gabinety.Booking.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using BookingStatus = Gabinety.Billing.BookingStatus;

namespace Gabinety.Booking.Tests;

/// <summary>Resolve on real PostgreSQL 17: the row lock makes two concurrent resolves of one booking one success and one 409, and one refund.</summary>
[Trait("tier", "integration")]
public class DisputesIntegrationTests : IAsyncLifetime
{
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
    }

    public async Task DisposeAsync() { await host.DisposeAsync(); await pg.DisposeAsync(); }

    BookingDbContext Db() => new(new DbContextOptionsBuilder<BookingDbContext>().UseNpgsql(cs).Options);

    [Fact]
    public async Task Two_concurrent_resolves_of_one_booking_give_one_200_and_one_409_and_one_refund()
    {
        for (var round = 0; round < 5; round++)
        {
            // Disputed stays in the no-overlap constraint: each round has its own slot, 4 h apart, inside the 60-day horizon of the test clock.
            var (id, _) = await Adm.Seed(host, startIn: TimeSpan.FromHours(-3 - 4 * round));
            var before = provider.Refunds.Count;
            var rs = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() => Adm.Resolve(host, id, Adm.Body("refund_full")))));
            var codes = rs.Select(r => (int)r.StatusCode).Order().ToArray();
            Assert.True(codes.SequenceEqual([200, 409]), $"round {round}: {string.Join(",", codes)}");

            await using var db = Db();
            Assert.Equal(BookingStatus.Resolved, (await db.Bookings.SingleAsync(b => b.Id == id)).Status);
            var payment = await db.Payments.SingleAsync(p => p.BookingId == id);
            Assert.Equal(PaymentStatus.Refunded, payment.Status);
            Assert.Equal(payment.AmountMinor, payment.RefundedMinor);
            Assert.Equal(1, await db.LedgerEntries.CountAsync(l => l.BookingId == id && l.Kind == LedgerKind.Refund));
            // the loser may or may not have reached the provider, but under one idempotency key, so the provider makes one refund of it
            Assert.Single(provider.Refunds.Skip(before).Select(r => r.IdempotencyKey).Distinct());
        }
    }

    [Fact]
    public async Task Two_concurrent_resolves_with_different_outcomes_resolve_once()
    {
        var (id, _) = await Adm.Seed(host, startIn: TimeSpan.FromHours(-30));
        var rs = await Task.WhenAll(
            Task.Run(() => Adm.Resolve(host, id, Adm.Body("release"))),
            Task.Run(() => Adm.Resolve(host, id, Adm.Body("refund_partial", 2500))));
        Assert.True(rs.Select(r => (int)r.StatusCode).Order().SequenceEqual([200, 409]));
        await using var db = Db();
        var row = await db.Bookings.SingleAsync(b => b.Id == id);
        Assert.Equal(BookingStatus.Resolved, row.Status);
        var refunded = (await db.Payments.SingleAsync(p => p.BookingId == id)).RefundedMinor;
        Assert.Equal(row.DisputeResolution == "release" ? 0 : 2500, refunded);
    }
}
