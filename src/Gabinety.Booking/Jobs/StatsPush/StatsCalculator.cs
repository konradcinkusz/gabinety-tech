using Gabinety.Booking.Features.Internal.PractitionerCancellation;
using Gabinety.Booking.Features.Slots;
using Gabinety.Booking.Persistence;
using Microsoft.EntityFrameworkCore;
using BookingStatus = Gabinety.Billing.BookingStatus;

namespace Gabinety.Booking.Jobs.StatsPush;

/// <summary>The four values Directory keeps per practitioner (CONTRACTS 3.6).</summary>
public sealed record BookingStats(int CompletedCount, int PractitionerCancelCount, int NoShowUpheldCount, int OpenSlots14d);

/// <summary>
/// Computes <see cref="BookingStats"/> for every practitioner that has bookings or availability rules.
/// <para>Counting rule (CONTRACTS 8.1, the "stats" column is the authority):</para>
/// <list type="bullet">
/// <item><c>completed_count</c> = rows in <c>Completed</c> + rows in <c>Resolved</c> with <c>dispute_resolution = 'release'</c> ("as Completed").
/// <c>ClientNoShow</c> and <c>LateCancelledByClient</c> count as completed "for money" only, and <c>Resolved/refund_partial</c> has no stats line: none of them is counted.</item>
/// <item><c>practitioner_cancel_count</c> = rows in <c>CancelledByPractitioner</c>, except those cancelled by the suspension or erasure cascade (<c>status_reason = practitioner_suspended</c>):
/// an administrator's action must not lower the reliability of a practitioner who is reinstated (ADR-0012, a deviation from the CONTRACTS 8.1 text).</item>
/// <item><c>no_show_upheld_count</c> = <c>Resolved</c> with <c>refund_full</c> and <c>dispute_kind = 'practitioner_no_show'</c>.</item>
/// <item><c>open_slots_14d</c> = bookable slots from today (Warsaw date) for 14 days, by <see cref="SlotCalculator"/>, for the shortest duration seen in the
/// practitioner's booking snapshots (Directory has no list-offerings lookup); 30 minutes (the grid minimum) when there is no booking.</item>
/// </list>
/// </summary>
public sealed class StatsCalculator(BookingDbContext db, IConfiguration config, TimeProvider time)
{
    public const int DefaultSlotMinutes = 30, WindowDays = 14;

    public async Task<Dictionary<Guid, BookingStats>> ComputeAsync(CancellationToken ct)
    {
        var groups = await db.Bookings.AsNoTracking()
            .GroupBy(b => new { b.PractitionerId, b.Status, b.DisputeResolution, b.DisputeKind, Cascade = b.StatusReason == FutureBookingCanceller.Reason })
            .Select(g => new { g.Key.PractitionerId, g.Key.Status, g.Key.DisputeResolution, g.Key.DisputeKind, g.Key.Cascade, N = g.Count(), Shortest = g.Min(b => (int)b.DurationMinutes) })
            .ToListAsync(ct);
        var withRules = (await db.Rules.AsNoTracking().Select(r => r.PractitionerId).Distinct().ToListAsync(ct)).ToHashSet();

        var calculator = new SlotCalculator(db, config, time);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), SlotCalculator.Warsaw).DateTime);
        var result = new Dictionary<Guid, BookingStats>();
        foreach (var id in groups.Select(g => g.PractitionerId).Concat(withRules).Distinct())
        {
            ct.ThrowIfCancellationRequested();
            var mine = groups.Where(g => g.PractitionerId == id).ToList();
            var completed = mine.Where(g => g.Status == BookingStatus.Completed || (g.Status == BookingStatus.Resolved && g.DisputeResolution == "release")).Sum(g => g.N);
            var cancelled = mine.Where(g => g.Status == BookingStatus.CancelledByPractitioner && !g.Cascade).Sum(g => g.N);
            var upheld = mine.Where(g => g.Status == BookingStatus.Resolved && g.DisputeResolution == "refund_full" && g.DisputeKind == "practitioner_no_show").Sum(g => g.N);

            var slots = 0;
            if (withRules.Contains(id))
            {
                var shortest = mine.Count == 0 ? 0 : mine.Min(g => g.Shortest);
                var minutes = shortest > 0 ? shortest : DefaultSlotMinutes;
                slots = (await calculator.ComputeAsync(id, TimeSpan.FromMinutes(minutes), today, today.AddDays(WindowDays - 1), ct)).Count;
            }
            result[id] = new BookingStats(completed, cancelled, upheld, slots);
        }
        return result;
    }
}
