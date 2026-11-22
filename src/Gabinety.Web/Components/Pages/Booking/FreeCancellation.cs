using Gabinety.Web.Components.Pages.Bookings;
using Gabinety.Web.Text;

namespace Gabinety.Web.Components.Pages.Booking;

/// <summary>
/// The deadline of free cancellation shown before the money is spent (UX-020, audit finding 6). The rule itself belongs to the Booking service: a client cancelling at least
/// <c>Gabinety.Billing.RefundPolicy.FullRefundWindow</c> before the start is refunded in full (CONTRACTS 9.2, an owner's constant). The web host does not reference Billing (architecture rule), so
/// the window is mirrored once, here, and <c>FreeCancellationTests</c> fails if it ever differs from the Billing value. This is a hint to the visitor; the service decides the refund.
/// </summary>
public static class FreeCancellation
{
    /// <summary>Mirror of <c>RefundPolicy.FullRefundWindow</c>.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromHours(24);

    /// <summary>The last instant of free cancellation, or null when it has already passed at <paramref name="now"/> (the start is the only input: the clock decides nothing else).</summary>
    public static DateTimeOffset? Until(DateTimeOffset start, DateTimeOffset now) => start - Window is var deadline && deadline > now ? deadline : null;

    /// <summary><c>Bezpłatne anulowanie do poniedziałek, 7 grudnia, 12:00.</c> in Europe/Warsaw, or the "no refund any more" sentence.</summary>
    public static string Line(DateTimeOffset start, DateTimeOffset now) => Until(start, now) is { } deadline
        ? string.Format(BookFlowText.Summary.FreeUntil, Day(deadline))
        : string.Format(BookFlowText.Summary.NoFreeCancel, (int)Window.TotalHours);

    static string Day(DateTimeOffset utc)
    {
        var local = TimeZoneInfo.ConvertTime(utc, BookingFormat.Warsaw);
        return $"{Pl.Format.Weekdays[(int)local.DayOfWeek]}, {local.Day} {Pl.Format.MonthsGenitive[local.Month - 1]}, {local:HH\\:mm}";
    }
}
