using Gabinety.Booking.Persistence;
using Gabinety.Contracts.Booking;
using Microsoft.EntityFrameworkCore;

namespace Gabinety.Booking.Features.Payouts;

/// <summary>
/// The reads behind the earnings figures: three narrow projections, each filtered by the practitioner's id, so another practitioner's rows are never loaded. The sum is
/// <see cref="EarningsCalculator"/>'s. Dates: a <c>Sent</c> payout by <c>sent_at</c>, a fee by the ledger entry's <c>created_at</c>, and pending and disputed money by the booking's
/// <c>start_utc</c> (the day of the consultation, as the practitioner's calendar and bookings list show it). All bounds are the half-open UTC range of the Warsaw period.
/// </summary>
public static class EarningsQueries
{
    public static async Task<EarningsTotals> TotalsAsync(BookingDbContext db, Guid practitionerId, EarningsPeriod period, CancellationToken ct)
    {
        var lo = period.StartUtc;
        var hi = period.EndUtcExclusive;
        var relevant = EarningsCalculator.RelevantStatuses;

        var sent = await db.Payouts.AsNoTracking()
            .Where(p => p.PractitionerId == practitionerId && p.Status == PayoutStatus.Sent && p.SentAt >= lo && p.SentAt < hi)
            .Select(p => new SentPayoutRow(p.BookingId, p.AmountMinor))
            .ToListAsync(ct);

        var bookings = await (
            from b in db.Bookings.AsNoTracking()
            where b.PractitionerId == practitionerId && b.StartUtc >= lo && b.StartUtc < hi
            join p in db.Payouts.AsNoTracking() on b.Id equals p.BookingId into payouts
            from p in payouts.DefaultIfEmpty()
            where relevant.Contains(b.Status) || (p != null && p.Status == PayoutStatus.Pending)
            select new EarningsBookingRow(b.Id, b.Status, b.PriceMinor, b.PayoutMinor,
                p == null ? (PayoutStatus?)null : p.Status, p == null ? (long?)null : p.AmountMinor)).ToListAsync(ct);

        var fees = await (
            from l in db.LedgerEntries.AsNoTracking()
            where l.Kind == LedgerKind.PlatformFee && l.CreatedAt >= lo && l.CreatedAt < hi
            join b in db.Bookings.AsNoTracking() on l.BookingId equals b.Id
            where b.PractitionerId == practitionerId
            select new FeeRow(b.Id, l.AmountMinor)).ToListAsync(ct);

        return EarningsCalculator.Compute(sent, bookings, fees);
    }
}
