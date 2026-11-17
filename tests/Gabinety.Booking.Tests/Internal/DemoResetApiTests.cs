using System.Net;
using BookingStatus = Gabinety.Billing.BookingStatus;
using Gabinety.Booking.Features.Bookings.Create;
using Gabinety.Booking.Features.Internal.TestClock;
using Gabinety.Booking.Features.Payments;
using Gabinety.Booking.Persistence;
using Gabinety.Contracts.Booking;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BookingEntity = Gabinety.Booking.Features.Bookings.Entities.Booking;

namespace Gabinety.Booking.Tests;

public class DemoResetApiTests
{
    /// <summary>Fake-profile host on its OWN in-memory database: the reset removes every demo- row, so it must not share "bookingdb" with parallel tests.</summary>
    static CreateHost Host()
    {
        var dbName = "demoreset-" + Guid.NewGuid().ToString("N");
        return ClockApiTests.FakeHost(h =>
        {
            var inner = h.Configure;
            h.Configure = s =>
            {
                inner?.Invoke(s);
                s.RemoveAll<DbContextOptions<BookingDbContext>>();
                s.RemoveAll(typeof(IDbContextOptionsConfiguration<BookingDbContext>));
                s.RemoveAll<BookingDbContext>();
                s.AddDbContext<BookingDbContext>(o => o.UseInMemoryDatabase(dbName));
            };
        });
    }

    sealed record Rows(Guid Booking, Guid Payment, Guid Ledger);

    static Rows SeedChain(BookingDbContext db, CreateHost h, string clientUser, string practitionerUser, string provider, string? eventId)
    {
        var now = h.Clock.GetUtcNow();
        var key = Guid.NewGuid();
        var id = Guid.NewGuid();
        db.Bookings.Add(new BookingEntity
        {
            Id = id, ClientUserId = clientUser, PractitionerId = h.Practitioner, PractitionerUserId = practitionerUser,
            OfferingId = h.Offering, OfferingTitle = "demo-offering", DurationMinutes = 60, PriceMinor = 10000,
            StartUtc = now.AddDays(2), EndUtc = now.AddDays(2).AddHours(1), Status = BookingStatus.Confirmed, BriefText = "demo-brief",
            IdempotencyKey = key, CreatedAt = now,
        });
        var pay = Guid.NewGuid();
        db.Payments.Add(new Payment { Id = pay, BookingId = id, Provider = "Mock", ProviderPaymentId = "mock_" + pay.ToString("N"), AmountMinor = 10000, Status = PaymentStatus.Succeeded, CreatedAt = now, UpdatedAt = now });
        var led = Guid.NewGuid();
        db.LedgerEntries.Add(new LedgerEntry { Id = led, BookingId = id, Kind = LedgerKind.PlatformFee, AmountMinor = 1500, PairKey = Guid.NewGuid().ToString("N"), CreatedAt = now });
        db.IdempotencyRecords.Add(new IdempotencyRecord { ClientUserId = clientUser, IdempotencyKey = key, RequestHash = "h", ResponseStatus = 201, ResponseBody = "{}", CreatedAt = now });
        if (eventId != null) db.WebhookEvents.Add(new WebhookEvent { Provider = provider, EventId = eventId, ReceivedAt = now, Outcome = WebhookOutcome.Applied });
        return new(id, pay, led);
    }

    static Task<HttpResponseMessage> Reset(CreateHost h, string? key = ClockApiTests.InternalKey) => ClockApiTests.PostRaw(h, "/internal/v1/demo/reset", "{}", key);

    static async Task<(int bookings, int payments, int ledger, int idem, int events)> Counts(CreateHost h) =>
        await h.Query(async db => (await db.Bookings.CountAsync(), await db.Payments.CountAsync(), await db.LedgerEntries.CountAsync(),
            await db.IdempotencyRecords.CountAsync(), await db.WebhookEvents.CountAsync()));

    [Fact]
    public async Task Reset_deletes_demo_bookings_with_their_payments_ledger_idempotency_and_webhook_rows_and_keeps_other_rows()
    {
        await using var h = Host();
        Rows asClient = null!, asPractitioner = null!, other = null!;
        await h.Seed(db =>
        {
            asClient = SeedChain(db, h, "demo-c1", "real-p1", "Mock", "demo-evt-1");
            asPractitioner = SeedChain(db, h, "real-c2", "demo-p2", "Mock", null);
            other = SeedChain(db, h, "real-c3", "real-p3", "Mock", "evt-real-1");
        });

        var r = await Reset(h);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var j = await r.Body();
        Assert.Equal(2, j.GetProperty("bookings").GetInt32());
        Assert.Equal(2, j.GetProperty("payments").GetInt32());
        Assert.Equal(2, j.GetProperty("ledgerEntries").GetInt32());
        Assert.Equal(1, j.GetProperty("idempotencyRecords").GetInt32()); // keyed by the demo client only
        Assert.Equal(1, j.GetProperty("webhookEvents").GetInt32());

        Assert.Equal((1, 1, 1, 2, 1), await Counts(h)); // the practitioner-side demo booking's idempotency record belongs to a real client and stays
        Assert.Equal(other.Booking, await h.Query(db => db.Bookings.Select(b => b.Id).SingleAsync()));
        Assert.Equal(other.Payment, await h.Query(db => db.Payments.Select(b => b.Id).SingleAsync()));
        Assert.Equal(other.Ledger, await h.Query(db => db.LedgerEntries.Select(b => b.Id).SingleAsync()));
        Assert.Equal("evt-real-1", await h.Query(db => db.WebhookEvents.Select(b => b.EventId).SingleAsync()));
    }

    [Fact]
    public async Task A_non_demo_booking_is_never_touched_even_when_the_only_row_present()
    {
        await using var h = Host();
        await h.Seed(db => SeedChain(db, h, "client-real", "practitioner-real", "Mock", "evt-x"));
        var j = await (await Reset(h)).Body();
        foreach (var t in new[] { "bookings", "payments", "ledgerEntries", "idempotencyRecords", "webhookEvents" })
            Assert.Equal(0, j.GetProperty(t).GetInt32());
        Assert.Equal((1, 1, 1, 1, 1), await Counts(h));
    }

    [Fact]
    public async Task A_namespace_lookalike_that_does_not_start_with_demo_dash_is_kept()
    {
        await using var h = Host();
        await h.Seed(db =>
        {
            SeedChain(db, h, "xdemo-c", "real-p", "Mock", "xdemo-evt");
            SeedChain(db, h, "Demo-c", "real-p", "Mock", null); // case matters: the namespace is lower case
        });
        Assert.Equal(0, (await (await Reset(h)).Body()).GetProperty("bookings").GetInt32());
        Assert.Equal(2, (await Counts(h)).bookings);
    }

    [Fact]
    public async Task Reset_is_idempotent_the_second_call_deletes_nothing()
    {
        await using var h = Host();
        await h.Seed(db => SeedChain(db, h, "demo-c1", "real-p1", "Mock", "demo-evt-1"));
        Assert.Equal(1, (await (await Reset(h)).Body()).GetProperty("bookings").GetInt32());
        var second = await Reset(h);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var j = await second.Body();
        foreach (var t in new[] { "bookings", "payments", "ledgerEntries", "idempotencyRecords", "webhookEvents" })
            Assert.Equal(0, j.GetProperty(t).GetInt32());
    }

    [Fact]
    public async Task Reset_leaves_availability_rules_and_exceptions_alone()
    {
        await using var h = Host();
        await h.WorkWeek();
        await h.Seed(db =>
        {
            SeedChain(db, h, "demo-c1", "demo-p1", "Mock", null);
            db.Exceptions.Add(new() { Id = Guid.NewGuid(), PractitionerId = h.Practitioner, DateLocal = new(2026, 12, 24) });
        });
        await Reset(h);
        Assert.Equal(5, await h.Query(db => db.Rules.CountAsync()));
        Assert.Equal(1, await h.Query(db => db.Exceptions.CountAsync()));
    }

    [Fact]
    public async Task A_missing_internal_key_is_401_and_deletes_nothing()
    {
        await using var h = Host();
        await h.Seed(db => SeedChain(db, h, "demo-c1", "real-p1", "Mock", null));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Reset(h, null)).StatusCode);
        Assert.Equal(1, (await Counts(h)).bookings);
    }
}
