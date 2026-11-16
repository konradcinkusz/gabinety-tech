using Gabinety.Billing;
using Gabinety.Booking.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Gabinety.Booking.Features.Slots;

/// <summary>The slot is taken: the exclusion constraint refused the insert. The creation endpoint (T-04.05) answers 409 slot_taken.</summary>
public sealed class SlotTakenException(Exception? inner = null) : Exception("The slot is taken.", inner);

/// <summary>
/// Helpers for creating bookings against the no-double-booking rule (CONTRACTS 4.1).
/// NOTE FOR T-04.05: the exclusion constraint cannot use now(), so a PendingPayment booking whose hold has EXPIRED but which the job has not yet
/// moved to Expired still occupies the slot IN THE DATABASE. The creation endpoint must, in one transaction, call <see cref="LockPractitionerAsync"/>,
/// then <see cref="ExpireStaleHoldsAsync"/>, save, and only then insert. The slots endpoint already ignores expired holds.
/// </summary>
public static class BookingSlotGuard
{
    public const string ConstraintName = "bookings_no_overlap";
    const string ExclusionViolation = "23P01";
    const string DeadlockDetected = "40P01";

    /// <summary>
    /// The insert lost the race for the slot: the exclusion constraint refused it (23P01), or PostgreSQL broke a deadlock between two concurrent
    /// inserts of overlapping intervals by aborting this one (40P01). Both were measured in CI: the loser sometimes gets the deadlock, and without a
    /// retrying execution strategy EF wraps it as InvalidOperationException ("likely due to a transient failure") around the DbUpdateException,
    /// so the whole inner-exception chain is searched. The creation path takes the practitioner lock first; this is the safety net behind that lock.
    /// </summary>
    public static bool IsSlotTaken(Exception e)
    {
        for (var x = e; x is not null; x = x.InnerException)
            if (x is PostgresException { SqlState: ExclusionViolation, ConstraintName: ConstraintName } or PostgresException { SqlState: DeadlockDetected })
                return true;
        return false;
    }

    /// <summary>SaveChanges, mapping the exclusion violation to <see cref="SlotTakenException"/>; anything else passes through.</summary>
    public static async Task SaveAsync(BookingDbContext db, CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (Exception e) when (e is not OperationCanceledException && IsSlotTaken(e)) { throw new SlotTakenException(e); }
    }

    /// <summary>Takes pg_advisory_xact_lock for the practitioner in the CURRENT transaction (the caller must have begun one); a no-op on other providers.</summary>
    public static async Task LockPractitionerAsync(BookingDbContext db, Guid practitionerId, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql()) return;
        await db.Database.ExecuteSqlInterpolatedAsync($"select pg_advisory_xact_lock(hashtext({practitionerId.ToString()}))", ct);
    }

    /// <summary>Sets Expired on the practitioner's PendingPayment rows whose hold ended at or before <paramref name="now"/>. Saves nothing: the caller saves.</summary>
    public static async Task<int> ExpireStaleHoldsAsync(BookingDbContext db, Guid practitionerId, DateTimeOffset now, CancellationToken ct)
    {
        var stale = await db.Bookings
            .Where(b => b.PractitionerId == practitionerId && b.Status == BookingStatus.PendingPayment && b.HoldExpiresAt <= now)
            .ToListAsync(ct);
        foreach (var b in stale) b.Status = BookingStatus.Expired;
        return stale.Count;
    }
}
