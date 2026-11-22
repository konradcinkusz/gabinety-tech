using System.Security.Claims;
using Gabinety.Billing;
using Gabinety.Booking.Features.Lifecycle.Core;
using Gabinety.Booking.Features.Payments;
using Gabinety.Booking.Persistence;
using Gabinety.Contracts.Booking;
using Gabinety.Contracts.Common;
using Gabinety.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using BookingEntity = Gabinety.Booking.Features.Bookings.Entities.Booking;
using BookingStatus = Gabinety.Billing.BookingStatus;

namespace Gabinety.Booking.Features.Lifecycle.Cancel;

/// <summary>
/// POST /api/v1/bookings/{id}/cancel (CONTRACTS 4.2, 8.1, 9.2). Only the client or the practitioner of the booking; anyone else gets 404.
/// The reason is user free text: it is stored in <c>status_reason</c> and nowhere else (no log, no exception, no metric).
/// Order: read and decide, ask the provider for the refund, then one transaction in <see cref="BookingTransitions"/>.
/// </summary>
public sealed class CancelFeature : IServiceFeature
{
    public const int MaxReasonLength = 500;

    public void Add(IHostApplicationBuilder builder) { }

    public void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost("/api/v1/bookings/{id:guid}/cancel", CancelAsync).RequireAuthorization("Authenticated");

    static async Task<IResult> CancelAsync(Guid id, CancelRequest? body, ClaimsPrincipal user, BookingDbContext db, BookingTransitions transitions,
        RefundCoordinator refunds, PartyNamesLookup names, TimeProvider time, CancellationToken ct)
    {
        var sub = user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (sub is null) return Results.Unauthorized();

        var booking = await db.Bookings.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id, ct);
        if (booking is null || (booking.ClientUserId != sub && booking.PractitionerUserId != sub))
            return PaymentWebhookFeature.Problem(ErrorCodes.NotFound, "Not found", 404); // a stranger learns nothing about whether it exists

        var reason = body?.Reason?.Trim();
        if (reason is { Length: > MaxReasonLength })
            return PaymentWebhookFeature.Problem(ErrorCodes.ValidationFailed, "Validation failed", 400, null,
                new() { ["reason"] = [$"The reason is at most {MaxReasonLength} characters."] });
        if (string.IsNullOrEmpty(reason)) reason = null;

        var byClient = booking.ClientUserId == sub;
        var trigger = CancelPlanner.TriggerFor(byClient);
        var now = time.GetUtcNow();
        Func<BookingEntity, bool> precondition = b => CancelPlanner.TimeRuleAllows(b, byClient, now);

        var plan = CancelPlanner.Plan(booking, byClient, now); // the same call that builds `cancelOutcome` in the booking view
        if (plan is null) return PaymentWebhookFeature.Problem(ErrorCodes.InvalidTransition, "Invalid transition", 409);

        RefundTicket? ticket = null;
        if (plan.Value.Refund > 0)
        {
            var payment = await db.Payments.AsNoTracking().SingleOrDefaultAsync(p => p.BookingId == id, ct);
            if (payment is null) return PaymentWebhookFeature.Problem(ErrorCodes.NotFound, "Not found", 404);
            // Lost a race: the booking was read as Confirmed, but the winning cancel has refunded the payment since. The transition is gone: 409, no provider call.
            if (payment.RefundedMinor + plan.Value.Refund > payment.AmountMinor)
                return PaymentWebhookFeature.Problem(ErrorCodes.InvalidTransition, "Invalid transition", 409);
            try { ticket = await refunds.RequestAsync(id, payment, plan.Value.Refund, plan.Value.Reason, ct); }
            catch (HttpRequestException) { return Unavailable(); }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return Unavailable(); }
        }

        var result = await transitions.ApplyAsync(new TransitionRequest(id, trigger, now)
        {
            Precondition = precondition,
            Effects = c =>
            {
                c.Booking.StatusReason = reason;
                if (ticket is not null) RefundCoordinator.Record(c, ticket);
            },
        }, ct);
        return result.ToErrorResult() ?? Results.Ok(BookingViewFeature.ToView(result.Booking!, result.Payment, sub, admin: false, names: (await names.ForAsync([result.Booking!], ct))[result.Booking!.Id]));
    }

    static IResult Unavailable() =>
        PaymentWebhookFeature.Problem(ErrorCodes.DependencyUnavailable, "Dependency unavailable", 503, "The payment provider is temporarily unavailable.");
}
