using Gabinety.Booking.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Gabinety.Booking.Features.Availability;

/// <summary>Reads and replaces a practitioner's availability. A replace is one SaveChanges, so it is one transaction: it all lands or none of it does.</summary>
public sealed class AvailabilityStore(BookingDbContext db)
{
    public async Task<(List<AvailabilityRule> Rules, List<AvailabilityException> Exceptions)> GetAsync(Guid practitionerId, CancellationToken ct)
    {
        var rules = await db.Rules.AsNoTracking().Where(r => r.PractitionerId == practitionerId)
            .OrderBy(r => r.IsoWeekday).ThenBy(r => r.StartLocal).ToListAsync(ct);
        var days = await db.Exceptions.AsNoTracking().Where(e => e.PractitionerId == practitionerId)
            .OrderBy(e => e.DateLocal).ToListAsync(ct);
        return (rules, days);
    }

    public async Task ReplaceAsync(Guid practitionerId, IReadOnlyList<AvailabilityRule> rules, IReadOnlyList<AvailabilityException> exceptions, CancellationToken ct)
    {
        db.Rules.RemoveRange(await db.Rules.Where(r => r.PractitionerId == practitionerId).ToListAsync(ct));
        db.Rules.AddRange(rules);

        // Dates are keyed, so only the difference is written: no delete-then-insert of one unique key inside a statement batch.
        var existing = await db.Exceptions.Where(e => e.PractitionerId == practitionerId).ToListAsync(ct);
        var wanted = exceptions.Select(e => e.DateLocal).ToHashSet();
        db.Exceptions.RemoveRange(existing.Where(e => !wanted.Contains(e.DateLocal)));
        var kept = existing.Select(e => e.DateLocal).ToHashSet();
        db.Exceptions.AddRange(exceptions.Where(e => !kept.Contains(e.DateLocal)));
        await db.SaveChangesAsync(ct);
    }
}
