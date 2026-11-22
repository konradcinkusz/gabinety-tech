using System.Security.Claims;
using Gabinety.Billing;
using Gabinety.Booking.Features.Bookings.Create;
using Gabinety.Booking.Features.Payments;
using Gabinety.Booking.Features.Slots;
using Gabinety.Booking.Notifications.Scheduling;
using Gabinety.Booking.Persistence;
using Gabinety.Contracts.Booking;
using Gabinety.Contracts.Common;
using Gabinety.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using BookingEntity = Gabinety.Booking.Features.Bookings.Entities.Booking;

namespace Gabinety.Booking.Features.Lifecycle.Reschedule;

/// <summary>
/// POST /api/v1/bookings/{id}/reschedule (CONTRACTS 4.2, 8.1). Moves a Confirmed booking to another free slot of the same offering, once, while at least
/// <see cref="RefundPolicy.FullRefundWindow"/> remains; the decision is <see cref="Billing.Reschedule.Decide"/>. Only the client moves a booking: a stranger gets 404,
/// the practitioner of the booking 403. No money moves: the payment and the ledger are only read.
/// One transaction under the practitioner's advisory lock (the one creation takes), the booking row locked FOR UPDATE; the new slot is checked with the
/// same <see cref="SlotCalculator"/> that creation uses, and the exclusion constraint behind the lock maps to 409 slot_taken. The old slot is released by the same update.
/// </summary>
public sealed class RescheduleFeature : IServiceFeature
{
    const int Attempts = 3;
    static readonly IResult Done = Results.NoContent(); // the body's answer when the booking moved; compared by reference

    public void Add(IHostApplicationBuilder builder) { }

    public void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost("/api/v1/bookings/{id:guid}/reschedule", RescheduleAsync).RequireAuthorization("Authenticated");

    static async Task<IResult> RescheduleAsync(Guid id, RescheduleRequest? body, ClaimsPrincipal user, BookingDbContext db, RescheduleNotifications notifications,
        PartyNamesLookup names, IConfiguration config, TimeProvider time, CancellationToken ct)
    {
        var sub = BookingViewFeature.CallerId(user);
        if (sub is null) return Results.Unauthorized();

        var read = await db.Bookings.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id, ct);
        if (read is null || (read.ClientUserId != sub && read.PractitionerUserId != sub))
            return PaymentWebhookFeature.Problem(ErrorCodes.NotFound, "Not found", 404); // a stranger learns nothing about whether it exists
        if (read.ClientUserId != sub) return PaymentWebhookFeature.Problem(ErrorCodes.Forbidden, "Forbidden", 403);

        if (body?.StartUtc is not { Offset.Ticks: 0 } requested)
            return PaymentWebhookFeature.Problem(ErrorCodes.ValidationFailed, "Validation failed", 422, null,
                new() { ["startUtc"] = ["startUtc is required and must be a UTC instant (offset Z)"] });
        var newStart = requested.ToUniversalTime();

        var now = time.GetUtcNow();
        var calculator = new SlotCalculator(db, config, time);
        BookingEntity? moved = null;
        IResult? answer = null;
        try
        {
            // A claim by the notification dispatcher (the optimistic token of a row) can race the re-timing of that row: read again and retry.
            for (var attempt = 1; answer is null; attempt++)
            {
                try
                {
                    answer = await CreateBookingFeature.InLockedTransaction(db, read.PractitionerId, async () =>
                    {
                        var booking = db.Database.IsNpgsql()
                            ? await db.Bookings.FromSql($"SELECT * FROM bookings WHERE id = {id} FOR UPDATE").SingleOrDefaultAsync(ct)
                            : await db.Bookings.SingleOrDefaultAsync(b => b.Id == id, ct);
                        if (booking is null) return PaymentWebhookFeature.Problem(ErrorCodes.NotFound, "Not found", 404);

                        switch (Billing.Reschedule.Decide(booking.Status, booking.StartUtc, now, booking.RescheduleCount))
                        {
                            case RescheduleDecision.NotConfirmed or RescheduleDecision.AlreadyRescheduled:
                                return PaymentWebhookFeature.Problem(ErrorCodes.InvalidTransition, "Invalid transition", 409);
                            case RescheduleDecision.InsideWindow:
                                return PaymentWebhookFeature.Problem(ErrorCodes.RescheduleNotAllowed, "Reschedule not allowed", 422);
                        }

                        await BookingSlotGuard.ExpireStaleHoldsAsync(db, booking.PractitionerId, now, ct);
                        await db.SaveChangesAsync(ct);

                        var duration = TimeSpan.FromMinutes(booking.DurationMinutes);
                        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(newStart, SlotCalculator.Warsaw).DateTime);
                        var offered = await calculator.ComputeAsync(booking.PractitionerId, duration, day, day.AddDays(1), ct, ignoreBookingId: booking.Id);
                        if (newStart == booking.StartUtc || !offered.Contains(newStart))
                        {
                            // The slot it already holds, or one the grid never offers, is 422; on the grid but taken is 409, as in creation.
                            var grid = await calculator.ComputeAsync(booking.PractitionerId, duration, day, day.AddDays(1), ct, withBusy: false);
                            return newStart != booking.StartUtc && grid.Contains(newStart)
                                ? PaymentWebhookFeature.Problem(ErrorCodes.SlotTaken, "Slot taken", 409)
                                : PaymentWebhookFeature.Problem(ErrorCodes.SlotUnavailable, "Slot unavailable", 422);
                        }

                        booking.StartUtc = newStart;
                        booking.EndUtc = newStart + duration;
                        booking.RescheduleCount++;
                        booking.RescheduledAt = now;
                        await notifications.ApplyAsync(booking, now, ct);
                        await BookingSlotGuard.SaveAsync(db, ct); // SlotTakenException when the exclusion constraint refuses (the safety net behind the lock)
                        moved = booking;
                        return Done;
                    }, ct);
                }
                catch (DbUpdateConcurrencyException) when (attempt < Attempts) { db.ChangeTracker.Clear(); }
            }
        }
        catch (SlotTakenException) { return PaymentWebhookFeature.Problem(ErrorCodes.SlotTaken, "Slot taken", 409); }

        if (!ReferenceEquals(answer, Done) || moved is null) return answer;
        var payment = await db.Payments.AsNoTracking().SingleOrDefaultAsync(p => p.BookingId == id, ct);
        return Results.Ok(BookingViewFeature.ToView(moved, payment, sub, admin: false, time, (await names.ForAsync([moved], ct))[moved.Id]));
    }
}
