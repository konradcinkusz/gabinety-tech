using Gabinety.Booking.Features.Bookings.Create;
using Gabinety.Booking.Features.Lifecycle.Core;
using Gabinety.Booking.Features.Payments;
using Gabinety.Contracts.Booking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BookingEntity = Gabinety.Booking.Features.Bookings.Entities.Booking;
using BookingStatus = Gabinety.Billing.BookingStatus;
using BookingTrigger = Gabinety.Billing.BookingTrigger;

namespace Gabinety.Booking.Tests;

/// <summary>Helpers for the fee and payout tests: bookings of any pair seeded in any status, and a transition applied through <see cref="BookingTransitions"/>.</summary>
static class Fee
{
    public static string NewClient() => "demo-" + Guid.NewGuid().ToString("N");

    /// <summary>A booking with a succeeded payment (and its charge) for the given pair, starting <paramref name="startIn"/> after the host clock.</summary>
    public static async Task<Guid> Seed(CreateHost h, BookingStatus status, string client, Guid practitioner, long price, TimeSpan? startIn = null, long refunded = 0, string? resolution = null)
    {
        var id = Guid.NewGuid();
        var start = h.Clock.GetUtcNow() + (startIn ?? TimeSpan.FromDays(2));
        await h.Seed(db =>
        {
            db.Bookings.Add(new BookingEntity
            {
                Id = id, ClientUserId = client, PractitionerId = practitioner, PractitionerUserId = "demo-practitioner-user", OfferingId = h.Offering,
                OfferingTitle = h.Title, DurationMinutes = 60, PriceMinor = price, StartUtc = start, EndUtc = start.AddHours(1), Status = status,
                BriefText = "demo-brief: a first review of my plan", IdempotencyKey = Guid.NewGuid(), CreatedAt = h.Clock.GetUtcNow(),
                DisputeResolution = resolution,
            });
            db.Payments.Add(new Payment
            {
                Id = Guid.NewGuid(), BookingId = id, Provider = "Mock", ProviderPaymentId = "pay_" + id.ToString("N"), AmountMinor = price, RefundedMinor = refunded,
                Status = PaymentStatus.Succeeded, CreatedAt = h.Clock.GetUtcNow(), UpdatedAt = h.Clock.GetUtcNow(),
            });
        });
        return id;
    }

    public static async Task<BookingTransitionResult> Apply(CreateHost h, Guid id, BookingTrigger trigger, Action<BookingTransitionContext>? effects = null)
    {
        using var scope = h.Services.CreateScope();
        var t = scope.ServiceProvider.GetRequiredService<BookingTransitions>();
        return await t.ApplyAsync(new TransitionRequest(id, trigger, h.Clock.GetUtcNow()) { Effects = effects }, default);
    }

    public static async Task<(long? Fee, long? Payout)> Amounts(CreateHost h, Guid id)
    {
        var b = await Life.Row(h, id);
        return (b.FeeMinor, b.PayoutMinor);
    }

    public static Task<List<LedgerEntry>> FeeRows(CreateHost h, string client, Guid practitioner) =>
        h.Query(db => db.LedgerEntries.AsNoTracking().Where(l => l.Kind == LedgerKind.PlatformFee && l.PairKey == client + ":" + practitioner).ToListAsync());
}
