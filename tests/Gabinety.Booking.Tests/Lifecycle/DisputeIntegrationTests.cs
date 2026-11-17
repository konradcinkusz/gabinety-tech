using System.Net;
using Gabinety.Booking.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using BookingStatus = Gabinety.Billing.BookingStatus;

namespace Gabinety.Booking.Tests;

/// <summary>Dispute on real PostgreSQL 17: the row lock makes two concurrent disputes of one booking one success and one 409.</summary>
[Trait("tier", "integration")]
public class DisputeIntegrationTests : IAsyncLifetime
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
    public async Task Two_concurrent_disputes_of_one_booking_give_one_200_and_one_409_and_the_winners_text_is_kept()
    {
        for (var round = 0; round < 10; round++)
        {
            // A Disputed booking stays in the no-overlap constraint, so each round gets its own slot, 4 h apart: 3 h to 39 h back, each ended
            // less than 48 h ago (inside the dispute window, and inside the 60-day horizon of the test clock).
            var id = await Life.Seed(host, BookingStatus.Confirmed, TimeSpan.FromHours(-3 - 4 * round));
            var texts = new[] { $"demo-dispute-A-{round:00}-text", $"demo-dispute-B-{round:00}-text" };
            var rs = await Task.WhenAll(texts.Select(t => Task.Run(() => DisputeApiTests.Dispute(host, id, Life.Client, new { kind = "other", text = t }))));
            var codes = rs.Select(r => (int)r.StatusCode).Order().ToArray();
            Assert.True(codes.SequenceEqual([200, 409]), $"round {round}: {string.Join(",", codes)}");
            var winner = Array.FindIndex(rs, r => r.StatusCode == HttpStatusCode.OK);
            await using var db = Db();
            var row = await db.Bookings.SingleAsync(b => b.Id == id);
            Assert.Equal(BookingStatus.Disputed, row.Status);
            Assert.Equal(texts[winner], row.DisputeText);
        }
    }
}
