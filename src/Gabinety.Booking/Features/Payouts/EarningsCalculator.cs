using System.Globalization;
using Gabinety.Billing;
using Gabinety.Booking.Features.Slots;

namespace Gabinety.Booking.Features.Payouts;

/// <summary>
/// The period of an earnings request (CONTRACTS 4.2): two Warsaw calendar days, both inclusive, so a 25-hour autumn day is whole. The default is the current month.
/// A missing bound follows the one that was given: only <c>from</c> means the end of its month, only <c>to</c> the start of its month, neither the current month.
/// </summary>
public readonly record struct EarningsPeriod(DateOnly From, DateOnly To)
{
    /// <summary>CONTRACTS 4.2: <c>to - from</c> is at most 366 days.</summary>
    public const int MaxSpanDays = 366;

    /// <summary>First instant of <see cref="From"/> in Warsaw, as UTC.</summary>
    public DateTimeOffset StartUtc => WarsawDayStart(From);

    /// <summary>First instant after <see cref="To"/> in Warsaw, as UTC: the range is half open, so no instant belongs to two adjacent periods.</summary>
    public DateTimeOffset EndUtcExclusive => WarsawDayStart(To.AddDays(1));

    public static bool TryResolve(string? from, string? to, DateTimeOffset now, out EarningsPeriod period, out Dictionary<string, string[]> errors)
    {
        errors = [];
        period = default;
        var f = Parse(from, "from", errors);
        var t = Parse(to, "to", errors);
        if (errors.Count > 0) return false;

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, SlotCalculator.Warsaw).DateTime);
        var first = f ?? MonthStart(t ?? today);
        var last = t ?? MonthEnd(f ?? today);

        if (last < first) errors["to"] = ["The to date is before from."];
        else if (last.DayNumber - first.DayNumber > MaxSpanDays) errors["to"] = [$"The range is at most {MaxSpanDays} days."];
        else if (first.Year < MinYear || last.Year > MaxYear) errors["from"] = [$"The dates are between {MinYear} and {MaxYear}."];
        if (errors.Count > 0) return false;

        period = new EarningsPeriod(first, last);
        return true;
    }

    const int MinYear = 2000, MaxYear = 2100;

    static DateOnly? Parse(string? value, string name, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrEmpty(value)) return null;
        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d;
        errors[name] = [$"The {name} date is yyyy-MM-dd."];
        return null;
    }

    static DateOnly MonthStart(DateOnly d) => new(d.Year, d.Month, 1);

    static DateOnly MonthEnd(DateOnly d) => new(d.Year, d.Month, DateTime.DaysInMonth(d.Year, d.Month));

    static DateTimeOffset WarsawDayStart(DateOnly day) =>
        new(TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue), SlotCalculator.Warsaw), TimeSpan.Zero);
}

/// <summary>A <c>Sent</c> payout whose <c>sent_at</c> is in the period: the booking it belongs to and its amount.</summary>
public sealed record SentPayoutRow(Guid BookingId, long AmountMinor);

/// <summary>
/// One of the practitioner's bookings that starts in the period, with its payout row when there is one. <see cref="PayoutMinor"/> is the amount stored on the booking when it
/// completed (price minus fee); the payout row's own amount is used when the row exists.
/// </summary>
public sealed record EarningsBookingRow(Guid BookingId, BookingStatus Status, long PriceMinor, long? PayoutMinor, PayoutStatus? PayoutStatus, long? PayoutAmountMinor);

/// <summary>A <c>PlatformFee</c> ledger entry created in the period, with the booking it was charged on.</summary>
public sealed record FeeRow(Guid BookingId, long AmountMinor);

/// <summary>The five figures of <c>GET /api/v1/me/earnings</c>: grosz as <see cref="long"/>, and the number of bookings behind the first three.</summary>
public sealed record EarningsTotals(long PaidOut, long Pending, long Disputed, long Fees, int Count);

/// <summary>
/// The arithmetic of the earnings figures (CONTRACTS 4.2), with no database and no clock. Integers only; every sum is <c>checked</c>, so an overflow throws instead of
/// wrapping. The query loads the candidate rows; what each row means is decided here:
/// <list type="bullet">
/// <item><c>paidOut</c>: the amounts of <c>Sent</c> payouts (the caller filtered them by <c>sent_at</c>).</item>
/// <item><c>pending</c>: the amount of a <c>Pending</c> payout, plus the stored payout of a <c>Completed</c>, <c>ClientNoShow</c> or <c>LateCancelledByClient</c> booking that has no payout row yet
/// (the payout job has not taken it up; a late cancellation keeps the practitioner's share, CONTRACTS 9.2). A <c>Failed</c> payout is in no figure and a <c>Sent</c> one is already
/// <c>paidOut</c>. A full refund (<c>CancelledByClient</c>, <c>CancelledByPractitioner</c>, <c>Expired</c>) leaves nothing to receive, so it is in none.</item>
/// <item><c>disputed</c>: the price of a <c>Disputed</c> booking.</item>
/// <item><c>fees</c>: the platform fee entries. The ledger is append-only, so a fee stays when its booking is later refunded in part.</item>
/// <item><c>count</c>: the distinct bookings behind <c>paidOut</c>, <c>pending</c> and <c>disputed</c>. CONTRACTS 4.2 does not define it; this is the recorded reading.</item>
/// </list>
/// </summary>
public static class EarningsCalculator
{
    /// <summary>Statuses the query has to load besides any booking with a <c>Pending</c> payout.</summary>
    public static readonly BookingStatus[] RelevantStatuses =
        [BookingStatus.Completed, BookingStatus.ClientNoShow, BookingStatus.LateCancelledByClient, BookingStatus.Disputed];

    public static EarningsTotals Compute(IEnumerable<SentPayoutRow> sent, IEnumerable<EarningsBookingRow> bookings, IEnumerable<FeeRow> fees)
    {
        long paidOut = 0, pending = 0, disputed = 0, feeTotal = 0;
        var counted = new HashSet<Guid>();
        checked
        {
            foreach (var p in sent) { paidOut += p.AmountMinor; counted.Add(p.BookingId); }
            foreach (var b in bookings)
            {
                if (b.PayoutStatus is PayoutStatus.Pending)
                {
                    pending += b.PayoutAmountMinor ?? 0;
                    counted.Add(b.BookingId);
                }
                else if (b.PayoutStatus is null)
                {
                    if (b.Status is BookingStatus.Completed or BookingStatus.ClientNoShow or BookingStatus.LateCancelledByClient)
                    {
                        pending += b.PayoutMinor ?? 0;
                        counted.Add(b.BookingId);
                    }
                    else if (b.Status is BookingStatus.Disputed)
                    {
                        disputed += b.PriceMinor;
                        counted.Add(b.BookingId);
                    }
                }
            }
            foreach (var f in fees) feeTotal += f.AmountMinor;
        }
        return new EarningsTotals(paidOut, pending, disputed, feeTotal, counted.Count);
    }
}
