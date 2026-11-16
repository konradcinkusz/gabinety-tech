using Gabinety.Billing;
using Gabinety.Booking.Persistence;
using Gabinety.Scheduling;
using Microsoft.EntityFrameworkCore;
using SchedulingException = Gabinety.Scheduling.AvailabilityException;
using SchedulingRule = Gabinety.Scheduling.AvailabilityRule;

namespace Gabinety.Booking.Features.Slots;

/// <summary>The slot computation of CONTRACTS 9.4 for one practitioner: shared by the slots endpoint and the booking creation, so both agree on what is bookable.</summary>
public sealed class SlotCalculator(BookingDbContext db, IConfiguration config, TimeProvider time)
{
    public static readonly TimeZoneInfo Warsaw = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");

    /// <param name="withBusy">False answers the bare grid (rules, exceptions, lead time, horizon) and ignores existing bookings: it tells "never offered" from "taken".</param>
    public async Task<IReadOnlyList<DateTimeOffset>> ComputeAsync(Guid practitionerId, TimeSpan duration, DateOnly fromDate, DateOnly toDate,
        CancellationToken ct, bool withBusy = true)
    {
        var now = time.GetUtcNow();
        var windowStart = new DateTimeOffset(fromDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(-1);
        var windowEnd = new DateTimeOffset(toDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(2);

        var rules = (await db.Rules.AsNoTracking().Where(r => r.PractitionerId == practitionerId).ToListAsync(ct))
            .Select(r => new SchedulingRule(r.IsoWeekday, r.StartLocal, r.EndLocal)).ToList();
        var days = (await db.Exceptions.AsNoTracking().Where(e => e.PractitionerId == practitionerId).ToListAsync(ct))
            .Select(e => new SchedulingException(e.DateLocal)).ToList();

        // An EXPIRED hold does not block here (the database constraint still counts it until it is moved to Expired: see BookingSlotGuard).
        var busy = !withBusy ? [] : (await db.Bookings.AsNoTracking()
                .Where(b => b.PractitionerId == practitionerId && b.StartUtc < windowEnd && b.EndUtc > windowStart
                    && (b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.Disputed
                        || (b.Status == BookingStatus.PendingPayment && b.HoldExpiresAt > now)))
                .Select(b => new { b.StartUtc, b.EndUtc }).ToListAsync(ct))
            .Select(b => new BusyInterval(b.StartUtc, b.EndUtc)).ToList();

        var options = new SlotOptions(
            MinLeadHours: config.GetValue("Booking:MinLeadHours", 12),
            HorizonDays: config.GetValue("Booking:HorizonDays", 60),
            BufferMinutes: config.GetValue("Booking:BufferMinutes", 15));
        return SlotGenerator.Generate(rules, days, duration, busy, now, fromDate, toDate, options, Warsaw);
    }
}
