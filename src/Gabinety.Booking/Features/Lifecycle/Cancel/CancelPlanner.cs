using Gabinety.Billing;
using Gabinety.Booking.Features.Lifecycle.Core;
using Gabinety.Contracts.Booking;
using Gabinety.Contracts.Common;
using BookingEntity = Gabinety.Booking.Features.Bookings.Entities.Booking;
using BookingStatus = Gabinety.Billing.BookingStatus;

namespace Gabinety.Booking.Features.Lifecycle.Cancel;

/// <summary>What a cancel will do: the refund in grosz (0 for none), the provider's reason and the outcome the UI tells the user.</summary>
public readonly record struct CancelPlan(long Refund, RefundReason Reason, CancelOutcomeKind Outcome)
{
    public CancelOutcomeDto ToDto() => new(Outcome, new MoneyDto(Refund));
}

/// <summary>
/// The one place that decides the result of a cancel (CONTRACTS 8.1, 9.2). <see cref="CancelFeature"/> performs it and <c>BookingViewFeature.ToView</c> shows it
/// before the user confirms (<c>cancelOutcome</c>), so the dialog and the money cannot disagree. Pure: no I/O, no clock of its own.
/// </summary>
public static class CancelPlanner
{
    /// <summary>The practitioner may cancel any time before the end; the state table cannot see that (BookingStateMachine). The client has no time limit.</summary>
    public static bool TimeRuleAllows(BookingEntity b, bool byClient, DateTimeOffset now) => byClient || now < b.EndUtc;

    public static BookingTrigger TriggerFor(bool byClient) => byClient ? BookingTrigger.ClientCancels : BookingTrigger.PractitionerCancels;

    /// <summary>Null when the state table or the time rule refuses the cancel, else its plan.</summary>
    public static CancelPlan? Plan(BookingEntity b, bool byClient, DateTimeOffset now)
    {
        if (!BookingStateMachine.Next(b.Status, TriggerFor(byClient), b.StartUtc, now).Allowed || !TimeRuleAllows(b, byClient, now)) return null;
        var money = byClient ? RefundPolicy.ForClientCancellation(b.StartUtc, now) : MoneyEvent.PractitionerCancelled;
        if (money == MoneyEvent.ClientCancelledLate) return new(0, RefundReason.ClientCancelledEarly, CancelOutcomeKind.NoRefund);
        var refund = RefundPolicy.Settle(money, Money.Pln(b.PriceMinor), rateBps: 0, pairAlreadyCharged: true).Refund.Minor;
        var kind = refund <= 0 ? CancelOutcomeKind.NoRefund : refund >= b.PriceMinor ? CancelOutcomeKind.FullRefund : CancelOutcomeKind.Partial;
        return new(refund, byClient ? RefundReason.ClientCancelledEarly : RefundReason.PractitionerCancelled, kind);
    }
}
