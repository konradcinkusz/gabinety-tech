using System.Text.Json;
using Gabinety.Web.Api;
using Gabinety.Web.Components.Shared;
using Gabinety.Web.Text;

namespace Gabinety.Web.Components.Pages.Bookings;

/// <summary>
/// What cancelling THIS booking would mean for the money (F-11). The page never works it out from the clock (the 24 hour rule of CONTRACTS 9.2 is the service's, on its clock, and a test clock moves it):
/// the Booking service sends it as <c>cancelOutcome</c> on the detail read. Until that field exists the dialog says the general rule; when it is there the dialog says the outcome. The field's shape is read
/// tolerantly: an object with <c>refundMinor</c>, <c>refund</c> or <c>refundAmount</c> (a number of grosz or a money object), or a word (<c>FullRefund</c>, <c>NoRefund</c>).
/// </summary>
public sealed record CancelOutcome(long RefundMinor, long PaidMinor)
{
    public bool IsFull => RefundMinor >= PaidMinor && PaidMinor > 0;
    public bool IsNone => RefundMinor <= 0;

    public static CancelOutcome? From(JsonElement? raw, MoneyDto price)
    {
        if (raw is not { } e || e.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
        long? refund = e.ValueKind switch
        {
            JsonValueKind.String => e.GetString()?.ToLowerInvariant() switch { "fullrefund" or "full" or "refund" => price.AmountMinor, "norefund" or "none" => 0, _ => null },
            JsonValueKind.Object => Minor(e, "refundMinor") ?? Minor(e, "refund") ?? Minor(e, "refundAmount"),
            _ => null,
        };
        return refund is { } r && r >= 0 && r <= price.AmountMinor ? new CancelOutcome(r, price.AmountMinor) : null;
    }

    static long? Minor(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n)) return n;
        return v.ValueKind == JsonValueKind.Object && v.TryGetProperty("amountMinor", out var m) && m.ValueKind == JsonValueKind.Number && m.TryGetInt64(out var a) ? a : null;
    }

    /// <summary>The sentence of the cancel dialog: the outcome when the booking carries one, else the general rule of the role.</summary>
    public static string Sentence(CancelOutcome? outcome, string role)
    {
        if (role == BookingClient.PractitionerRole)
            return outcome is null ? BookingsText.Cancel.PractitionerRule : string.Format(BookingsText.Cancel.PractitionerOutcome, Money.Pln(outcome.PaidMinor));
        return outcome switch
        {
            null => BookingsText.Cancel.ClientRule,
            { IsNone: true } => BookingsText.Cancel.OutcomeNone,
            { IsFull: true } => string.Format(BookingsText.Cancel.OutcomeFull, Money.Pln(outcome.RefundMinor)),
            _ => string.Format(BookingsText.Cancel.OutcomePartial, Money.Pln(outcome.RefundMinor), Money.Pln(outcome.PaidMinor)),
        };
    }
}
