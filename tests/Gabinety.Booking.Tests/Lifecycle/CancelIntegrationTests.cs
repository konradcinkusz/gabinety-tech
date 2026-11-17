using System.Net;
using Gabinety.Booking.Persistence;
using Gabinety.Contracts.Booking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using BookingStatus = Gabinety.Billing.BookingStatus;

namespace Gabinety.Booking.Tests;

/// <summary>Cancel on real PostgreSQL 17: the row lock makes two concurrent cancels of one booking one success and one 409.</summary>
[Trait("tier", "integration")]
public class CancelIntegrationTests : IAsyncLifetime
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
    public async Task Two_concurrent_cancels_of_one_booking_give_one_200_one_409_and_one_refund_row()
    {
        for (var round = 0; round < 10; round++)
        {
            var before = provider.Refunds.Count;
            // Starts inside the 60-day horizon of the test clock; 2 days ahead is an early cancel, so a refund is due.
            var id = await Life.Seed(host, BookingStatus.Confirmed, TimeSpan.FromDays(2));
            var who = round % 2 == 0 ? new[] { Life.Client, Life.Client } : new[] { Life.Client, Life.Practitioner };
            var rs = await Task.WhenAll(who.Select(u => Task.Run(() => Life.Cancel(host, id, u))));
            var codes = rs.Select(r => (int)r.StatusCode).Order().ToArray();
            Assert.True(codes.SequenceEqual([200, 409]), $"round {round}: {string.Join(",", codes)}");
            await using var db = Db();
            Assert.Equal(1, await db.LedgerEntries.CountAsync(l => l.BookingId == id && l.Kind == LedgerKind.Refund));
            var pay = await db.Payments.SingleAsync(p => p.BookingId == id);
            Assert.Equal((PaymentStatus.Refunded, 10000L), (pay.Status, pay.RefundedMinor));
            Assert.True(provider.Refunds.Count - before >= 1);
            Assert.True((await db.Bookings.SingleAsync(b => b.Id == id)).Status is BookingStatus.CancelledByClient or BookingStatus.CancelledByPractitioner);
        }
    }
}
