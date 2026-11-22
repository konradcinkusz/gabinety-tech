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

    [Fact]
    public async Task The_Warsaw_day_range_filters_on_start_utc_and_the_cursor_pages_it_on_PostgreSQL()
    {
        var client = "demo-c-" + Guid.NewGuid().ToString("N");
        // 2026-10-25 in Warsaw is the 25 hour day: it runs from 2026-10-24T22:00Z to 2026-10-25T23:00Z
        var starts = new (string Note, DateTimeOffset At, bool InRange)[]
        {
            ("23:59:59 on the 24th", new DateTimeOffset(2026, 10, 24, 21, 59, 59, TimeSpan.Zero), false),
            ("00:00 on the 25th, CEST", new DateTimeOffset(2026, 10, 24, 22, 0, 0, TimeSpan.Zero), true),
            ("02:30 on the 25th, CET", new DateTimeOffset(2026, 10, 25, 1, 30, 0, TimeSpan.Zero), true),
            ("23:30 on the 25th, CET", new DateTimeOffset(2026, 10, 25, 22, 30, 0, TimeSpan.Zero), true),
            ("00:30 on the 26th", new DateTimeOffset(2026, 10, 25, 23, 30, 0, TimeSpan.Zero), false),
        };
        var rows = starts.Select(s => (s.Note, s.InRange, Id: Guid.NewGuid(), s.At)).ToList();
        await host.Seed(db =>
        {
            foreach (var r in rows)
                db.Bookings.Add(new BookingEntity
                {
                    Id = r.Id, ClientUserId = client, PractitionerId = Guid.NewGuid(), PractitionerUserId = "demo-p", OfferingId = host.Offering, OfferingTitle = "demo-offering",
                    DurationMinutes = 60, PriceMinor = 10000, StartUtc = r.At, EndUtc = r.At.AddHours(1), Status = BookingStatus.Expired, BriefText = "demo-brief",
                    IdempotencyKey = Guid.NewGuid(), CreatedAt = host.Clock.GetUtcNow(),
                });
        });
        var expected = rows.Where(r => r.InRange).OrderByDescending(r => r.At).Select(r => r.Id).ToList();

        var seen = new List<Guid>();
        string? cursor = null;
        for (var page = 0; page < 10; page++)
        {
            var r = await host.ClientFor(client).GetAsync("/api/v1/me/bookings?role=client&from=2026-10-25&to=2026-10-25&limit=2" + (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor)));
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            var body = await r.Body();
            seen.AddRange(body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()));
            if (!body.TryGetProperty("nextCursor", out var next)) break;
            cursor = next.GetString();
        }
        Assert.Equal(expected, seen);
    }
}
