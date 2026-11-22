using Gabinety.Billing;
using Gabinety.Booking.Features.Lifecycle.Core;
using Gabinety.Booking.Features.Payments;
using Gabinety.Booking.Persistence;
using Gabinety.Contracts.Common;
using Gabinety.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using BookingStatus = Gabinety.Billing.BookingStatus;

namespace Gabinety.Booking.Features.Internal.PractitionerCancellation;

/// <param name="Cancelled">Confirmed future bookings moved to <c>CancelledByPractitioner</c> by this call.</param>
/// <param name="HoldsExpired">Unpaid holds moved to <c>Expired</c> by this call.</param>
/// <param name="Failed">Bookings that could not be settled this time (provider down, payment unusable); the next call retries them.</param>
public sealed record CancelFutureBookingsResult(int Cancelled, int HoldsExpired, int Failed);

/// <summary>
/// What happens to a practitioner's bookings when Directory suspends or erases them (ADR-0012, RUNBOOK 9). It goes through the one transition machinery
/// (<see cref="BookingTransitions"/>), so the rules are those of a practitioner cancel (CONTRACTS 8.1, 9.2): a <c>Confirmed</c> booking that has not started becomes
/// <c>CancelledByPractitioner</c> with a full refund at any distance from the start, no fee, no payout, and the client is told with the <c>Cancelled</c> notification.
/// A hold (<c>PendingPayment</c>) is expired at once, so a payment that arrives later meets the existing "paid after expiry" refund rule. A booking that has started
/// (or is over) is left to run its course and to the completion job. One transaction per booking; the provider is asked before it (no lock across a network call), with
/// the same idempotency key as any practitioner cancel, so a repeated or resumed call is one refund.
/// </summary>
public sealed class FutureBookingCanceller(IServiceScopeFactory scopes, TimeProvider time, ILogger<FutureBookingCanceller> log)
{
    /// <summary>The <c>status_reason</c> of a booking cancelled by suspension or erasure. A code, never user text.</summary>
    public const string Reason = "practitioner_suspended";

    public async Task<CancelFutureBookingsResult> CancelAsync(Guid practitionerId, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        List<(Guid Id, BookingStatus Status)> open;
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
            open = (await db.Bookings.AsNoTracking()
                    .Where(b => b.PractitionerId == practitionerId && ((b.Status == BookingStatus.Confirmed && b.StartUtc > now) || b.Status == BookingStatus.PendingPayment))
                    .OrderBy(b => b.StartUtc).ThenBy(b => b.Id).Select(b => new { b.Id, b.Status }).ToListAsync(ct))
                .Select(b => (b.Id, b.Status)).ToList();
        }

        int cancelled = 0, expired = 0, failed = 0;
        foreach (var (id, status) in open)
        {
            try
            {
                using var scope = scopes.CreateScope(); // one context, one transaction per booking
                var applied = status == BookingStatus.PendingPayment ? await ExpireAsync(scope, id, now, ct) : await CancelOneAsync(scope, id, now, ct);
                if (applied == Outcome.Applied) { if (status == BookingStatus.PendingPayment) expired++; else cancelled++; }
                else if (applied == Outcome.Failed) failed++;
            }
            catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException or InvalidOperationException or DbUpdateException) && !ct.IsCancellationRequested)
            {
                failed++;
                log.LogError("Could not settle booking {BookingId} of a suspended practitioner: {ExceptionType}", id, ex.GetType().Name);
            }
        }
        if (cancelled + expired + failed > 0)
            log.LogInformation("Suspension of practitioner {PractitionerId}: cancelled {Cancelled}, expired {Expired} hold(s), failed {Failed}", practitionerId, cancelled, expired, failed);
        return new CancelFutureBookingsResult(cancelled, expired, failed);
    }

    enum Outcome { Applied, Skipped, Failed }

    static async Task<Outcome> ExpireAsync(IServiceScope scope, Guid id, DateTimeOffset now, CancellationToken ct)
    {
        var transitions = scope.ServiceProvider.GetRequiredService<BookingTransitions>();
        var result = await transitions.ApplyAsync(new TransitionRequest(id, BookingTrigger.HoldExpired, now), ct);
        return result.Applied ? Outcome.Applied : Outcome.Skipped; // moved meanwhile: nothing left to do
    }

    static async Task<Outcome> CancelOneAsync(IServiceScope scope, Guid id, DateTimeOffset now, CancellationToken ct)
    {
        var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        var transitions = scope.ServiceProvider.GetRequiredService<BookingTransitions>();
        var refunds = scope.ServiceProvider.GetRequiredService<RefundCoordinator>();

        var booking = await db.Bookings.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id, ct);
        if (booking is null || booking.Status != BookingStatus.Confirmed || booking.StartUtc <= now) return Outcome.Skipped;
        var payment = await db.Payments.AsNoTracking().SingleOrDefaultAsync(p => p.BookingId == id, ct);
        if (payment is null) return Outcome.Failed;

        // the amount is what the policy says for a practitioner cancel (all of the price), not a number of this feature
        var refund = RefundPolicy.Settle(MoneyEvent.PractitionerCancelled, Money.Pln(booking.PriceMinor), rateBps: 0, pairAlreadyCharged: true).Refund.Minor;
        RefundTicket? ticket = null;
        if (refund > 0 && payment.RefundedMinor < payment.AmountMinor) // a refund the provider already holds is not asked for twice
            ticket = await refunds.RequestAsync(id, payment, Math.Min(refund, payment.AmountMinor - payment.RefundedMinor), RefundReason.PractitionerCancelled, ct);

        var result = await transitions.ApplyAsync(new TransitionRequest(id, BookingTrigger.PractitionerCancels, now)
        {
            Precondition = b => now < b.EndUtc && b.StartUtc > now,
            Effects = c =>
            {
                c.Booking.StatusReason = Reason;
                if (ticket is not null) RefundCoordinator.Record(c, ticket);
            },
        }, ct);
        return result.Applied ? Outcome.Applied : Outcome.Skipped;
    }
}

/// <summary>
/// POST /internal/v1/practitioners/{id}/cancel-future-bookings, caller: Directory (suspension, erasure). <c>200 { cancelled, holdsExpired, failed }</c>; <c>503
/// dependency_unavailable</c> when some booking could not be settled (the others were), so the caller reports it and may repeat the call. Idempotent; an unknown
/// practitioner is <c>200</c> with zeros.
/// </summary>
public sealed class CancelFutureBookingsFeature : IServiceFeature
{
    public void Add(IHostApplicationBuilder builder) => builder.Services.AddScoped<FutureBookingCanceller>();

    public void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost("/internal/v1/practitioners/{id:guid}/cancel-future-bookings", CancelAsync).RequireAuthorization("Internal");

    static async Task<IResult> CancelAsync(Guid id, FutureBookingCanceller canceller, CancellationToken ct)
    {
        var result = await canceller.CancelAsync(id, ct);
        return result.Failed == 0
            ? Results.Ok(result)
            : PaymentWebhookFeature.Problem(ErrorCodes.DependencyUnavailable, "Dependency unavailable", 503,
                "Some bookings could not be cancelled yet (the payment provider is unavailable); repeat the call.");
    }
}
