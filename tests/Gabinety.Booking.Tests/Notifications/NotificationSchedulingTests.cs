using Gabinety.Booking.Features.Lifecycle.Core;
using Gabinety.Booking.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BookingStatus = Gabinety.Billing.BookingStatus;
using BookingTrigger = Gabinety.Billing.BookingTrigger;

namespace Gabinety.Booking.Tests;

/// <summary>T-04.09b: the notification rows every transition writes in its own transaction (CONTRACTS 8.1).</summary>
public class NotificationSchedulingTests
{
    const string Client = "demo-client-sched", Practitioner = "demo-practitioner-user"; // the practitioner user id Fee.Seed gives its bookings

    static Task<List<Notification>> Rows(CreateHost h, Guid id) =>
        h.Query(db => db.Notifications.AsNoTracking().Where(n => n.BookingId == id).ToListAsync());

    static async Task<List<NotificationKind>> Kinds(CreateHost h, Guid id) => (await Rows(h, id)).Select(r => r.Kind).ToList();

    /// <summary>A booking in <paramref name="status"/> starting in <paramref name="startIn"/> (one hour long) with its succeeded payment.</summary>
    static Task<Guid> Seed(CreateHost h, TimeSpan startIn, BookingStatus status = BookingStatus.PendingPayment) =>
        Fee.Seed(h, status, Client, Guid.NewGuid(), 10000, startIn);

    static async Task<Guid> ConfirmedWithRows(CreateHost h, TimeSpan startIn)
    {
        var id = await Seed(h, startIn);
        Assert.True((await Fee.Apply(h, id, BookingTrigger.PaymentSucceeded)).Applied);
        return id;
    }

    static Task MarkSent(CreateHost h, Guid id, NotificationKind kind, string? user = null) =>
        h.Seed(db => db.Notifications.Single(n => n.BookingId == id && n.Kind == kind && (user == null || n.ToUserId == user)).SentAt = h.Clock.GetUtcNow());

    [Fact]
    public async Task Confirming_schedules_two_confirmations_two_reminders_and_one_review_request_at_their_times()
    {
        using var h = new CreateHost();
        var id = await Seed(h, TimeSpan.FromDays(3));
        var b = await Life.Row(h, id);

        Assert.True((await Fee.Apply(h, id, BookingTrigger.PaymentSucceeded)).Applied);

        var rows = await Rows(h, id);
        Assert.Equal(5, rows.Count);
        var confirmations = rows.Where(r => r.Kind == NotificationKind.BookingConfirmed).ToList();
        Assert.Equal(new[] { Client, Practitioner }.Order(), confirmations.Select(r => r.ToUserId).Order());
        Assert.All(confirmations, r => Assert.Equal(h.Clock.GetUtcNow(), r.DueAt));
        var r24 = Assert.Single(rows, r => r.Kind == NotificationKind.Reminder24h);
        var r1 = Assert.Single(rows, r => r.Kind == NotificationKind.Reminder1h);
        var review = Assert.Single(rows, r => r.Kind == NotificationKind.ReviewRequest);
        Assert.Equal((Client, b.StartUtc.AddHours(-24)), (r24.ToUserId, r24.DueAt));
        Assert.Equal((Client, b.StartUtc.AddHours(-1)), (r1.ToUserId, r1.DueAt));
        Assert.Equal((Client, b.EndUtc.AddHours(2)), (review.ToUserId, review.DueAt));
        Assert.All(rows, r => Assert.Equal((null, 0), (r.SentAt, r.Attempts)));
    }

    [Fact]
    public async Task A_transition_that_is_not_a_confirmation_schedules_nothing()
    {
        using var h = new CreateHost();
        var id = await Seed(h, TimeSpan.FromDays(3));
        Assert.True((await Fee.Apply(h, id, BookingTrigger.HoldExpired)).Applied);
        Assert.Empty(await Rows(h, id));
    }

    [Fact]
    public async Task Confirming_when_the_rows_are_already_saved_adds_no_duplicate()
    {
        using var h = new CreateHost();
        var id = await ConfirmedWithRows(h, TimeSpan.FromDays(3));
        await h.Seed(db => db.Bookings.Single(b => b.Id == id).Status = BookingStatus.PendingPayment); // a repair or replay that confirms the booking again
        Assert.True((await Fee.Apply(h, id, BookingTrigger.PaymentSucceeded)).Applied);
        Assert.Equal(5, (await Rows(h, id)).Count);
    }

    [Fact]
    public async Task A_booking_confirmed_less_than_24_h_before_the_start_gets_no_24_h_reminder()
    {
        using var h = new CreateHost();
        var id = await Seed(h, TimeSpan.FromHours(20));
        await Fee.Apply(h, id, BookingTrigger.PaymentSucceeded);
        var kinds = await Kinds(h, id);
        Assert.DoesNotContain(NotificationKind.Reminder24h, kinds);
        Assert.Contains(NotificationKind.Reminder1h, kinds);
        Assert.Contains(NotificationKind.ReviewRequest, kinds);
        Assert.Equal(2, kinds.Count(k => k == NotificationKind.BookingConfirmed));
    }

    [Fact]
    public async Task A_booking_confirmed_less_than_1_h_before_the_start_gets_no_reminders()
    {
        using var h = new CreateHost();
        var id = await Seed(h, TimeSpan.FromMinutes(30));
        await Fee.Apply(h, id, BookingTrigger.PaymentSucceeded);
        var kinds = await Kinds(h, id);
        Assert.DoesNotContain(NotificationKind.Reminder24h, kinds);
        Assert.DoesNotContain(NotificationKind.Reminder1h, kinds);
        Assert.Contains(NotificationKind.ReviewRequest, kinds);
    }

    [Theory]
    [InlineData(BookingTrigger.ClientCancels, 72)]          // CancelledByClient
    [InlineData(BookingTrigger.ClientCancels, 5)]           // LateCancelledByClient: no review either
    public async Task A_cancellation_removes_the_unsent_reminders_and_review_request_and_keeps_a_sent_confirmation(BookingTrigger trigger, int startInHours)
    {
        using var h = new CreateHost();
        var id = await ConfirmedWithRows(h, TimeSpan.FromHours(startInHours));
        await MarkSent(h, id, NotificationKind.BookingConfirmed, Client);

        Assert.True((await Fee.Apply(h, id, trigger)).Applied);

        var rows = await Rows(h, id);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(NotificationKind.BookingConfirmed, r.Kind));
        Assert.NotNull(rows.Single(r => r.ToUserId == Client).SentAt);
    }

    [Fact]
    public async Task A_practitioner_cancel_also_removes_the_unsent_rows_and_tells_the_client_with_Cancelled_due_now()
    {
        using var h = new CreateHost();
        var id = await ConfirmedWithRows(h, TimeSpan.FromHours(72));
        await MarkSent(h, id, NotificationKind.BookingConfirmed, Client);

        Assert.True((await Fee.Apply(h, id, BookingTrigger.PractitionerCancels)).Applied);

        var rows = await Rows(h, id);
        Assert.Equal([NotificationKind.BookingConfirmed, NotificationKind.BookingConfirmed, NotificationKind.Cancelled], rows.Select(r => r.Kind).Order());
        var cancelled = rows.Single(r => r.Kind == NotificationKind.Cancelled);
        Assert.Equal((Client, h.Clock.GetUtcNow(), null), (cancelled.ToUserId, cancelled.DueAt, cancelled.SentAt));
    }

    [Fact]
    public async Task A_cancellation_by_the_client_sends_the_client_no_Cancelled_notification()
    {
        using var h = new CreateHost();
        var id = await ConfirmedWithRows(h, TimeSpan.FromHours(72));
        Assert.True((await Fee.Apply(h, id, BookingTrigger.ClientCancels)).Applied);
        Assert.DoesNotContain(NotificationKind.Cancelled, await Kinds(h, id));
    }

    [Fact]
    public async Task A_cancellation_keeps_an_already_sent_reminder()
    {
        using var h = new CreateHost();
        var id = await ConfirmedWithRows(h, TimeSpan.FromDays(3));
        await MarkSent(h, id, NotificationKind.Reminder24h);
        await Fee.Apply(h, id, BookingTrigger.PractitionerCancels);
        var kinds = await Kinds(h, id);
        Assert.Contains(NotificationKind.Reminder24h, kinds);
        Assert.DoesNotContain(NotificationKind.Reminder1h, kinds);
    }

    [Fact]
    public async Task A_client_no_show_removes_the_review_request()
    {
        using var h = new CreateHost();
        var id = await ConfirmedWithRows(h, TimeSpan.FromDays(3));
        await Fee.Apply(h, id, BookingTrigger.ClientNoShowReported);
        Assert.DoesNotContain(NotificationKind.ReviewRequest, await Kinds(h, id));
    }

    [Fact]
    public async Task Dispute_and_resolution_keep_the_scheduled_rows()
    {
        using var h = new CreateHost();
        var id = await ConfirmedWithRows(h, TimeSpan.FromDays(3));
        await Fee.Apply(h, id, BookingTrigger.ClientDisputes);
        await Fee.Apply(h, id, BookingTrigger.AdminResolves, c => c.Booking.DisputeResolution = "release");
        Assert.Equal(5, (await Rows(h, id)).Count);
    }

    [Fact]
    public async Task The_rows_are_written_in_the_transitions_transaction_so_a_hook_failing_after_ours_leaves_none()
    {
        using var h = new CreateHost();
        var configure = h.Configure;
        h.Configure = s => { configure?.Invoke(s); s.AddScoped<IBookingTransitionHook, ThrowingHook>(); };
        var id = await Seed(h, TimeSpan.FromDays(3));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Fee.Apply(h, id, BookingTrigger.PaymentSucceeded));
        Assert.Empty(await Rows(h, id));
        Assert.Equal(BookingStatus.PendingPayment, (await Life.Row(h, id)).Status);
    }

    [Fact]
    public async Task A_scheduled_reminder_is_sent_by_the_dispatcher_when_its_time_comes_and_not_before()
    {
        using var nh = new NotificationHost();
        var id = await Seed(nh.Host, TimeSpan.FromDays(3));
        await Fee.Apply(nh.Host, id, BookingTrigger.PaymentSucceeded);

        Assert.Equal(2, await nh.Run()); // the two confirmations are due now, nothing else
        Assert.All(nh.Sender.Sent, m => Assert.Equal(NotificationKind.BookingConfirmed, m.Kind));

        nh.Host.Clock.Advance(TimeSpan.FromDays(2)); // start - 24 h
        Assert.Equal(1, await nh.Run());
        Assert.Contains(nh.Sender.Sent, m => m.Kind == NotificationKind.Reminder24h);
    }

    [Fact]
    public async Task Scheduling_logs_neither_a_user_id_nor_the_brief()
    {
        using var h = new CreateHost();
        var id = await Seed(h, TimeSpan.FromDays(3));
        await Fee.Apply(h, id, BookingTrigger.PaymentSucceeded);
        var text = string.Join('\n', Adm.LogLines(h));
        Assert.DoesNotContain("demo-brief", text);
        Assert.DoesNotContain(Client, text);
        Assert.DoesNotContain(Practitioner, text);
    }

    sealed class ThrowingHook : IBookingTransitionHook
    {
        public Task OnTransitionAsync(BookingTransitionContext c, CancellationToken ct) =>
            c.To == BookingStatus.Confirmed ? throw new InvalidOperationException("demo failure") : Task.CompletedTask;
    }
}
