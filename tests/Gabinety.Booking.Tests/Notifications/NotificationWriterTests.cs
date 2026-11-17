using Gabinety.Booking.Notifications;
using Gabinety.Booking.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BookingEntity = Gabinety.Booking.Features.Bookings.Entities.Booking;

namespace Gabinety.Booking.Tests;

public class NotificationWriterTests
{
    static readonly DateTimeOffset Due = new(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Scheduling_the_same_booking_kind_and_user_twice_adds_one_row()
    {
        using var t = new NotificationHost();
        var booking = new BookingEntity { Id = Guid.NewGuid() };
        using (var scope = t.Host.Services.CreateScope())
        {
            var writer = scope.ServiceProvider.GetRequiredService<INotificationWriter>();
            Assert.True(writer.Schedule(booking, NotificationKind.Reminder24h, "demo-client", Due));
            Assert.False(writer.Schedule(booking, NotificationKind.Reminder24h, "demo-client", Due.AddHours(1)));
            await scope.ServiceProvider.GetRequiredService<BookingDbContext>().SaveChangesAsync();
        }
        var rows = await t.Host.Query(db => db.Notifications.AsNoTracking().Where(n => n.BookingId == booking.Id).ToListAsync());
        var row = Assert.Single(rows);
        Assert.Equal((NotificationKind.Reminder24h, "demo-client", Due, 0, (DateTimeOffset?)null), (row.Kind, row.ToUserId, row.DueAt, row.Attempts, row.SentAt));
    }

    [Fact]
    public async Task A_different_user_or_kind_is_a_different_row_and_Schedule_does_not_save()
    {
        using var t = new NotificationHost();
        var booking = new BookingEntity { Id = Guid.NewGuid() };
        using var scope = t.Host.Services.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<INotificationWriter>();
        Assert.True(writer.Schedule(booking, NotificationKind.BookingConfirmed, "demo-client", Due));
        Assert.True(writer.Schedule(booking, NotificationKind.BookingConfirmed, "demo-practitioner", Due));
        Assert.True(writer.Schedule(booking, NotificationKind.Reminder1h, "demo-client", Due));
        Assert.Empty(await t.Host.Query(db => db.Notifications.Where(n => n.BookingId == booking.Id).ToListAsync())); // nothing saved yet
        await scope.ServiceProvider.GetRequiredService<BookingDbContext>().SaveChangesAsync();
        Assert.Equal(3, (await t.Host.Query(db => db.Notifications.Where(n => n.BookingId == booking.Id).ToListAsync())).Count);
    }

    [Fact]
    public async Task ScheduleAsync_adds_nothing_when_the_row_was_saved_by_an_earlier_transaction()
    {
        using var t = new NotificationHost();
        var booking = new BookingEntity { Id = Guid.NewGuid() };
        t.Add(booking.Id, NotificationKind.Cancelled, "demo-client");
        using var scope = t.Host.Services.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<INotificationWriter>();
        Assert.False(await writer.ScheduleAsync(booking, NotificationKind.Cancelled, "demo-client", Due, default));
        Assert.True(await writer.ScheduleAsync(booking, NotificationKind.Cancelled, "demo-practitioner", Due, default));
        await scope.ServiceProvider.GetRequiredService<BookingDbContext>().SaveChangesAsync();
        Assert.Equal(2, (await t.Host.Query(db => db.Notifications.Where(n => n.BookingId == booking.Id).ToListAsync())).Count);
    }

    [Fact]
    public async Task CancelUnsent_removes_only_unsent_rows_of_the_named_kinds_of_that_booking()
    {
        using var t = new NotificationHost();
        var booking = Guid.NewGuid();
        var other = Guid.NewGuid();
        var r24 = t.Add(booking, NotificationKind.Reminder24h, "demo-client", Due);
        var r1 = t.Add(booking, NotificationKind.Reminder1h, "demo-practitioner", Due);
        var r1Sent = t.Add(booking, NotificationKind.Reminder1h, "demo-client", Due, sentAt: Due);
        var review = t.Add(booking, NotificationKind.ReviewRequest, "demo-client", Due);
        var otherBooking = t.Add(other, NotificationKind.Reminder24h, "demo-client", Due);

        using (var scope = t.Host.Services.CreateScope())
        {
            var writer = scope.ServiceProvider.GetRequiredService<INotificationWriter>();
            Assert.Equal(2, await writer.CancelUnsentAsync(booking, [NotificationKind.Reminder24h, NotificationKind.Reminder1h], default));
            await scope.ServiceProvider.GetRequiredService<BookingDbContext>().SaveChangesAsync();
        }
        var left = (await t.Host.Query(db => db.Notifications.AsNoTracking().Where(n => n.BookingId == booking || n.BookingId == other).Select(n => n.Id).ToListAsync())).ToHashSet();
        Assert.Equal(new HashSet<Guid> { r1Sent.Id, review.Id, otherBooking.Id }, left);
        Assert.DoesNotContain(r24.Id, left);
        Assert.DoesNotContain(r1.Id, left);
    }

    [Fact]
    public async Task CancelUnsent_also_removes_a_row_scheduled_in_the_same_unsaved_context()
    {
        using var t = new NotificationHost();
        var booking = new BookingEntity { Id = Guid.NewGuid() };
        using var scope = t.Host.Services.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<INotificationWriter>();
        writer.Schedule(booking, NotificationKind.Reminder24h, "demo-client", Due);
        Assert.Equal(1, await writer.CancelUnsentAsync(booking.Id, [NotificationKind.Reminder24h], default));
        await scope.ServiceProvider.GetRequiredService<BookingDbContext>().SaveChangesAsync();
        Assert.Empty(await t.Host.Query(db => db.Notifications.Where(n => n.BookingId == booking.Id).ToListAsync()));
    }

    [Fact]
    public void The_model_maps_the_table_the_unique_key_and_the_kind_as_a_string()
    {
        using var db = new BookingDbContextFactory().CreateDbContext([]);
        var e = db.Model.FindEntityType(typeof(Notification))!;
        Assert.Equal("notifications", e.GetTableName());
        Assert.Contains(e.GetIndexes(), i => i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual(["BookingId", "Kind", "ToUserId"]));
        Assert.Equal(typeof(string), e.FindProperty("Kind")!.GetProviderClrType());
        Assert.Equal(["attempts", "booking_id", "due_at", "id", "kind", "sent_at", "to_user_id"],
            e.GetProperties().Select(p => p.GetColumnName()).Order().ToArray());
    }
}
