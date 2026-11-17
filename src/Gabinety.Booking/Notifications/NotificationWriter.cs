using Gabinety.Booking.Persistence;
using Microsoft.EntityFrameworkCore;
using BookingEntity = Gabinety.Booking.Features.Bookings.Entities.Booking;

namespace Gabinety.Booking.Notifications;

/// <summary>
/// What an <c>IBookingTransitionHook</c> calls inside a transition's transaction (T-04.09b). Writes to the CURRENT scoped <see cref="BookingDbContext"/>,
/// which is the one the transition saves: nothing here saves.
/// </summary>
public interface INotificationWriter
{
    /// <summary>Adds the row to the context without saving. False (and no row) when the same (booking, kind, user) is already tracked in this context.</summary>
    bool Schedule(BookingEntity booking, NotificationKind kind, string toUserId, DateTimeOffset dueAt);

    /// <summary>As <see cref="Schedule"/> but also false when the row is already saved: use it when a transition may run for a booking that was scheduled before.</summary>
    Task<bool> ScheduleAsync(BookingEntity booking, NotificationKind kind, string toUserId, DateTimeOffset dueAt, CancellationToken ct);

    /// <summary>Removes the unsent rows (saved or just scheduled) of the named kinds for the booking, without saving. Sent rows stay. Returns the number removed.</summary>
    Task<int> CancelUnsentAsync(Guid bookingId, IReadOnlyCollection<NotificationKind> kinds, CancellationToken ct);
}

public sealed class NotificationWriter(BookingDbContext db) : INotificationWriter
{
    public bool Schedule(BookingEntity booking, NotificationKind kind, string toUserId, DateTimeOffset dueAt)
    {
        var id = booking.Id;
        if (db.Notifications.Local.Any(n => n.BookingId == id && n.Kind == kind && n.ToUserId == toUserId)) return false;
        db.Notifications.Add(new Notification { Id = Guid.NewGuid(), BookingId = id, Kind = kind, ToUserId = toUserId, DueAt = dueAt });
        return true;
    }

    public async Task<bool> ScheduleAsync(BookingEntity booking, NotificationKind kind, string toUserId, DateTimeOffset dueAt, CancellationToken ct)
    {
        var id = booking.Id;
        if (db.Notifications.Local.Any(n => n.BookingId == id && n.Kind == kind && n.ToUserId == toUserId)) return false;
        if (await db.Notifications.AnyAsync(n => n.BookingId == id && n.Kind == kind && n.ToUserId == toUserId, ct)) return false;
        return Schedule(booking, kind, toUserId, dueAt);
    }

    public async Task<int> CancelUnsentAsync(Guid bookingId, IReadOnlyCollection<NotificationKind> kinds, CancellationToken ct)
    {
        var named = kinds.ToList();
        var saved = await db.Notifications.Where(n => n.BookingId == bookingId && n.SentAt == null && named.Contains(n.Kind)).ToListAsync(ct);
        var pending = db.Notifications.Local.Where(n => n.BookingId == bookingId && n.SentAt == null && named.Contains(n.Kind));
        var doomed = saved.Concat(pending).Distinct().ToList(); // a tracked row appears in both lists: one instance
        db.Notifications.RemoveRange(doomed);
        return doomed.Count;
    }
}
