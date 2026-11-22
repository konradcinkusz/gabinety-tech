using Gabinety.Web.Api;
using Gabinety.Web.Components.Shared;
using Gabinety.Web.Text;

namespace Gabinety.Web.Components.Pages.Bookings;

/// <summary>
/// What cancelling THIS booking would mean for the money (F-11). The page never works it out from the clock (the 24 hour rule of CONTRACTS 9.2 is the service's, on its clock, and a test clock moves it):
/// the Booking service sends it as <c>cancelOutcome</c> on the booking read, only while <c>canCancel</c>. Without the field the dialog says the general rule; with it, ONLY the outcome (the amount returned): the general rule is not repeated.
/// </summary>
public sealed record CancelOutcome(long RefundMinor, long PaidMinor)
{
    public bool IsFull => RefundMinor >= PaidMinor && PaidMinor > 0;
    public bool IsNone => RefundMinor <= 0;

    /// <summary>The amount is what counts; a refund outside 0 to the price is not believed (the general rule stays).</summary>
    public static CancelOutcome? From(CancelOutcomeDto? dto, MoneyDto price) =>
        dto is { Refund: { } refund } && refund.AmountMinor >= 0 && refund.AmountMinor <= price.AmountMinor ? new CancelOutcome(refund.AmountMinor, price.AmountMinor) : null;

    /// <summary>A client who would get nothing back is warned (danger); every other sentence only informs.</summary>
    public static bool Warns(CancelOutcome? outcome, string role) => role != BookingClient.PractitionerRole && outcome is { IsNone: true };

    /// <summary>The sentence of the cancel dialog: the outcome when the booking carries one, else the general rule of the role.</summary>
    public static string Sentence(CancelOutcome? outcome, string role)
    {
        if (role == BookingClient.PractitionerRole)
            return outcome is null ? BookingsText.Cancel.PractitionerRule : string.Format(BookingsText.Cancel.PractitionerOutcome, Money.Pln(outcome.RefundMinor));
        return outcome switch
        {
            null => BookingsText.Cancel.ClientRule,
            { IsNone: true } => BookingsText.Cancel.OutcomeNone,
            { IsFull: true } => string.Format(BookingsText.Cancel.OutcomeFull, Money.Pln(outcome.RefundMinor)),
            _ => string.Format(BookingsText.Cancel.OutcomePartial, Money.Pln(outcome.RefundMinor), Money.Pln(outcome.PaidMinor)),
        };
    }
}
