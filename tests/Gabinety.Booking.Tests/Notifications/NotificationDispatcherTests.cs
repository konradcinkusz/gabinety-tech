using Gabinety.Booking.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gabinety.Booking.Tests;

public class NotificationDispatcherTests
{
    [Fact]
    public async Task A_due_notification_is_sent_once_and_sent_at_is_set()
    {
        using var t = new NotificationHost();
        var booking = await t.AddBooking();
        var n = t.Add(booking.Id, NotificationKind.BookingConfirmed, "demo-client");

        Assert.Equal(1, await t.Run());
        Assert.Equal(0, await t.Run());
        t.Host.Clock.Advance(TimeSpan.FromHours(5));
        Assert.Equal(0, await t.Run());

        var msg = Assert.Single(t.Sender.For("demo-client"));
        Assert.Equal("demo-person@demo.invalid", msg.ToAddress);
        var row = await t.Row(n.Id);
        Assert.Equal(new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero), row.SentAt);
        Assert.Equal(1, row.Attempts);
    }

    [Fact]
    public async Task A_notification_that_is_not_yet_due_waits_for_its_time()
    {
        using var t = new NotificationHost();
        var booking = await t.AddBooking();
        var n = t.Add(booking.Id, NotificationKind.Reminder1h, "demo-client", t.Now.AddHours(1));
        Assert.Equal(0, await t.Run());
        t.Host.Clock.Advance(TimeSpan.FromMinutes(59));
        Assert.Equal(0, await t.Run());
        t.Host.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(1, await t.Run());
        Assert.NotNull((await t.Row(n.Id)).SentAt);
    }

    [Fact]
    public async Task A_failing_sender_is_retried_with_backoff_and_stops_after_five_attempts()
    {
        using var t = new NotificationHost();
        t.Sender.AlwaysFail = true;
        var booking = await t.AddBooking();
        var n = t.Add(booking.Id, NotificationKind.Reminder24h, "demo-client");
        var start = t.Now;

        for (var attempt = 1; attempt <= NotificationDispatcher.MaxAttempts; attempt++)
        {
            Assert.Equal(0, await t.Run());
            Assert.Equal(attempt, t.Sender.Calls);
            var row = await t.Row(n.Id);
            Assert.Equal(attempt, row.Attempts);
            Assert.Null(row.SentAt);
            if (attempt == NotificationDispatcher.MaxAttempts) break;

            var wait = NotificationDispatcher.Backoff[attempt - 1];
            await t.Run(); // not due yet: no new call
            Assert.Equal(attempt, t.Sender.Calls);
            t.Host.Clock.Advance(wait - TimeSpan.FromSeconds(1));
            await t.Run();
            Assert.Equal(attempt, t.Sender.Calls);
            t.Host.Clock.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.True(t.Now - start <= TimeSpan.FromHours(24), "all five attempts happen within 24 h");
        t.Host.Clock.Advance(TimeSpan.FromDays(30));
        await t.Run();
        Assert.Equal(NotificationDispatcher.MaxAttempts, t.Sender.Calls);
        Assert.Equal(NotificationDispatcher.MaxAttempts, (await t.Row(n.Id)).Attempts);
    }

    [Theory]
    [InlineData(NotificationKind.Reminder1h)]
    [InlineData(NotificationKind.Reminder24h)]
    public async Task A_reminder_retried_after_backoff_past_the_start_is_given_up_and_never_sent(NotificationKind kind)
    {
        using var t = new NotificationHost();
        var booking = await t.AddBooking(edit: b => { b.StartUtc = t.Now.AddHours(5); b.EndUtc = b.StartUtc.AddHours(1); });
        var late = t.Add(booking.Id, kind, "demo-client");
        t.Sender.AlwaysFail = true;
        Assert.Equal(0, await t.Run()); // fails, next attempt in 10 min
        t.Sender.AlwaysFail = false;
        t.Host.Clock.Advance(TimeSpan.FromHours(6)); // past the start (+5 h) before the retry runs
        var calls = t.Sender.Calls;
        Assert.Equal(0, await t.Run());

        Assert.Equal(calls, t.Sender.Calls); // nothing was sent
        var row = await t.Row(late.Id);
        Assert.Null(row.SentAt);
        Assert.Equal(NotificationDispatcher.MaxAttempts, row.Attempts); // the give-up status: never selected again
        t.Host.Clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(0, await t.Run());
        Assert.Equal(calls, t.Sender.Calls);
        Assert.Contains(t.Host.Logs.Lines, l => l.Contains(late.Id.ToString()) && l.Contains(kind.ToString()) && l.Contains("start"));
        Assert.DoesNotContain(t.Host.Logs.Lines, l => l.Contains("demo-person@demo.invalid"));
    }

    [Fact]
    public async Task A_reminder_is_sent_up_to_the_start_and_given_up_from_the_start_on()
    {
        using var t = new NotificationHost();
        var booking = await t.AddBooking(edit: b => { b.StartUtc = t.Now.AddMinutes(30); b.EndUtc = b.StartUtc.AddHours(1); });
        var before = t.Add(booking.Id, NotificationKind.Reminder1h, "demo-before", t.Now);
        var atStart = t.Add(booking.Id, NotificationKind.Reminder1h, "demo-at-start", t.Now.AddMinutes(30));

        Assert.Equal(1, await t.Run());
        Assert.NotNull((await t.Row(before.Id)).SentAt);
        t.Host.Clock.Advance(TimeSpan.FromMinutes(30)); // now == start: the consultation has begun
        Assert.Equal(0, await t.Run());
        Assert.Equal(1, t.Sender.Calls);
        var row = await t.Row(atStart.Id);
        Assert.Null(row.SentAt);
        Assert.Equal(NotificationDispatcher.MaxAttempts, row.Attempts);
    }

    [Theory]
    [InlineData(NotificationKind.BookingConfirmed)]
    [InlineData(NotificationKind.ReviewRequest)]
    [InlineData(NotificationKind.Cancelled)]
    [InlineData(NotificationKind.DisputeOpened)]
    [InlineData(NotificationKind.DisputeResolved)]
    [InlineData(NotificationKind.PayoutSent)]
    public async Task Other_kinds_are_still_sent_after_the_booking_start(NotificationKind kind)
    {
        using var t = new NotificationHost();
        var booking = await t.AddBooking(edit: b => { b.StartUtc = t.Now.AddHours(-3); b.EndUtc = b.StartUtc.AddHours(1); });
        var n = t.Add(booking.Id, kind, "demo-client");

        Assert.Equal(1, await t.Run());
        Assert.NotNull((await t.Row(n.Id)).SentAt);
        Assert.Equal(1, (await t.Row(n.Id)).Attempts);
    }

    [Fact]
    public async Task A_notification_that_fails_once_and_then_succeeds_is_sent_with_two_attempts()
    {
        using var t = new NotificationHost();
        t.Sender.FailFirst = 1;
        var booking = await t.AddBooking();
        var n = t.Add(booking.Id, NotificationKind.Cancelled, "demo-client");
        Assert.Equal(0, await t.Run());
        t.Host.Clock.Advance(NotificationDispatcher.Backoff[0]);
        Assert.Equal(1, await t.Run());
        var row = await t.Row(n.Id);
        Assert.Equal(2, row.Attempts);
        Assert.Equal(t.Now, row.SentAt);
    }

    [Fact]
    public async Task A_missing_address_is_a_failed_attempt_and_nothing_is_sent()
    {
        using var t = new NotificationHost();
        t.Contacts.Default = null;
        var booking = await t.AddBooking();
        var n = t.Add(booking.Id, NotificationKind.BookingConfirmed, "demo-client");

        Assert.Equal(0, await t.Run());
        Assert.Equal(0, t.Sender.Calls);
        var row = await t.Row(n.Id);
        Assert.Equal(1, row.Attempts);
        Assert.Null(row.SentAt);

        t.Contacts.Addresses["demo-client"] = "demo-late@demo.invalid"; // the address turns up: the retry succeeds
        t.Host.Clock.Advance(NotificationDispatcher.Backoff[0]);
        Assert.Equal(1, await t.Run());
        Assert.Equal("demo-late@demo.invalid", Assert.Single(t.Sender.Sent).ToAddress);
    }

    [Fact]
    public async Task A_notification_of_an_unknown_booking_is_a_failed_attempt()
    {
        using var t = new NotificationHost();
        var n = t.Add(Guid.NewGuid(), NotificationKind.Cancelled, "demo-client");
        Assert.Equal(0, await t.Run());
        Assert.Equal(0, t.Sender.Calls);
        Assert.Equal(1, (await t.Row(n.Id)).Attempts);
    }

    [Fact]
    public async Task A_cycle_sends_a_bounded_batch_oldest_first()
    {
        using var t = new NotificationHost();
        var booking = await t.AddBooking();
        var rows = Enumerable.Range(0, NotificationDispatcher.BatchSize + 10)
            .Select(i => t.Add(booking.Id, NotificationKind.Reminder24h, $"demo-user-{i:D3}", t.Now.AddMinutes(-1000 + i))).ToList();

        Assert.Equal(NotificationDispatcher.BatchSize, await t.Run());
        var unsent = rows.Where(r => t.Row(r.Id).GetAwaiter().GetResult().SentAt == null).Select(r => r.ToUserId).Order().ToList();
        Assert.Equal(rows.Skip(NotificationDispatcher.BatchSize).Select(r => r.ToUserId).ToList(), unsent); // the newest ten wait
        Assert.Equal(10, await t.Run());
    }

    [Fact]
    public async Task Two_dispatchers_at_once_send_each_notification_exactly_once()
    {
        var db = "notif-" + Guid.NewGuid().ToString("N");
        using var a = new NotificationHost(db);
        var shared = new RecordingEmailSender { Yield = true };
        using var c = new NotificationHost(db, services: s => { s.RemoveAll<IEmailSender>(); s.AddSingleton<IEmailSender>(shared); });
        using var d = new NotificationHost(db, services: s => { s.RemoveAll<IEmailSender>(); s.AddSingleton<IEmailSender>(shared); });
        var booking = await a.AddBooking();
        var ids = Enumerable.Range(0, 30).Select(i => a.Add(booking.Id, NotificationKind.Reminder1h, $"demo-user-{i:D3}").Id).ToList();

        var counts = await Task.WhenAll(c.Run(), d.Run(), c.Run(), d.Run());

        Assert.Equal(30, counts.Sum());
        Assert.Equal(30, shared.Sent.Count);
        Assert.Equal(30, shared.Sent.Select(m => m.ToUserId).Distinct().Count());
        foreach (var id in ids) Assert.NotNull((await a.Row(id)).SentAt);
    }
}
