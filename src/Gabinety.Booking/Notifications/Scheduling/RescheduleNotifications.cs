using Gabinety.Booking.Persistence;
using Microsoft.EntityFrameworkCore;
using BookingEntity = Gabinety.Booking.Features.Bookings.Entities.Booking;

namespace Gabinety.Booking.Notifications.Scheduling;

/// <summary>
/// The notification rows of a reschedule (CONTRACTS 8.1, T-11.10), written in the reschedule's own transaction; nothing here saves.
/// The unsent reminders and the review request move to the new start with the offsets <see cref="NotificationSchedulingHook"/> gives them at confirmation;
/// a reminder whose new time is not in the future is removed instead (the same rule as at confirmation: a reminder after the start would be wrong).
/// A row that was already sent stays as it is. <c>Rescheduled</c> goes to both parties, due now.
/// </summary>
public sealed class RescheduleNotifications(BookingDbContext db, INotificationWriter writer)
{
    public async Task ApplyAsync(BookingEntity booking, DateTimeOffset now, CancellationToken ct)
    {
        var due = new Dictionary<NotificationKind, DateTimeOffset>
        {
            [NotificationKind.Reminder24h] = booking.StartUtc.AddHours(-24),
            [NotificationKind.Reminder1h] = booking.StartUtc.AddHours(-1),
            [NotificationKind.ReviewRequest] = booking.EndUtc + NotificationSchedulingHook.ReviewDelay,
        };
        var kinds = due.Keys.ToList();
        var unsent = await db.Notifications.Where(n => n.BookingId == booking.Id && n.SentAt == null && kinds.Contains(n.Kind)).ToListAsync(ct);
        foreach (var row in unsent)
        {
            if (row.Kind != NotificationKind.ReviewRequest && due[row.Kind] <= now) db.Notifications.Remove(row);
            else row.DueAt = due[row.Kind];
        }

        await writer.ScheduleAsync(booking, NotificationKind.Rescheduled, booking.ClientUserId, now, ct);
        await writer.ScheduleAsync(booking, NotificationKind.Rescheduled, booking.PractitionerUserId, now, ct);
    }
}
