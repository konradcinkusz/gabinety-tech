using System.Net;
using System.Text.Json;
using Gabinety.Booking.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using BookingEntity = Gabinety.Booking.Features.Bookings.Entities.Booking;
using BookingStatus = Gabinety.Billing.BookingStatus;

namespace Gabinety.Booking.Tests;

/// <summary>The keyset predicate of the booking list (start, then <c>Guid.CompareTo</c>) translated by Npgsql: rows with one start page without loss or repeat.</summary>
[Trait("tier", "integration")]
public class MyBookingsIntegrationTests : IAsyncLifetime
{
    PostgreSqlContainer pg = null!;
    CreateHost host = null!;

    public async Task InitializeAsync()
    {
        pg = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await pg.StartAsync();
        host = Life.Host(new RecordingProvider(), h =>
        {
            h.Settings["Database:Provider"] = "PostgreSQL";
            h.Settings["ConnectionStrings:bookingdb"] = pg.GetConnectionString();
        });
        host.CreateClient();
        await host.Services.GetRequiredService<MigrationState>().WaitForCompletionAsync(TimeSpan.FromSeconds(60));
    }

    public async Task DisposeAsync() { await host.DisposeAsync(); await pg.DisposeAsync(); }

    [Fact]
    public async Task Rows_with_equal_starts_page_exactly_once_on_PostgreSQL()
    {
        var client = "demo-c-" + Guid.NewGuid().ToString("N");
        var start = host.Clock.GetUtcNow().AddDays(3);
        var ids = Enumerable.Range(0, 7).Select(_ => Guid.NewGuid()).ToList();
        // Distinct practitioner ids and spaced starts are not needed for the keyset; the exclusion constraint is avoided by using non-active statuses.
        await host.Seed(db =>
        {
            foreach (var id in ids)
                db.Bookings.Add(new BookingEntity
                {
                    Id = id, ClientUserId = client, PractitionerId = Guid.NewGuid(), PractitionerUserId = "demo-p", OfferingId = host.Offering, OfferingTitle = "demo-offering",
                    DurationMinutes = 60, PriceMinor = 10000, StartUtc = start, EndUtc = start.AddHours(1), Status = BookingStatus.Expired, BriefText = "demo-brief",
                    IdempotencyKey = Guid.NewGuid(), CreatedAt = host.Clock.GetUtcNow(),
                });
        });

        var seen = new List<Guid>();
        string? cursor = null;
        for (var page = 0; page < 10; page++)
        {
            var r = await host.ClientFor(client).GetAsync("/api/v1/me/bookings?role=client&limit=3" + (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor)));
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            var body = await r.Body();
            seen.AddRange(body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()));
            if (!body.TryGetProperty("nextCursor", out var next)) break;
            cursor = next.GetString();
        }
        Assert.Equal(ids.Count, seen.Count);
        Assert.Equal(ids.Order(), seen.Order());
    }
}
