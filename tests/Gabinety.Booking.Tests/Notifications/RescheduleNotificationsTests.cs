using System.Net;
using Gabinety.Booking.Notifications;
using BookingStatus = Gabinety.Billing.BookingStatus;

namespace Gabinety.Booking.Tests;

/// <summary>T-11.10: what a reschedule does to the notification rows (CONTRACTS 8.1): the unsent ones follow the new start, the sent ones stay, both parties are told once.</summary>
public class RescheduleNotificationsTests
{
    static readonly TimeSpan H25 = TimeSpan.FromHours(25);

    [Fact]
    public async Task Both_parties_get_one_Rescheduled_due_now()
    {
        var (h, _, id) = await Resched.Confirmed(H25);
        await using var _ = h;
        Assert.Equal(HttpStatusCode.OK, (await Resched.Move(h, id, Life.Client, Resched.Wednesday10)).StatusCode);

        var told = (await Resched.Rows(h, id)).Where(n => n.Kind == NotificationKind.Rescheduled).ToList();
        Assert.Equal(new[] { Life.Client, Life.Practitioner }.Order(), told.Select(n => n.ToUserId).Order());
        Assert.All(told, n => Assert.Equal((h.Clock.GetUtcNow(), (DateTimeOffset?)null, 0), (n.DueAt, n.SentAt, n.Attempts)));
    }

    [Fact]
    public async Task The_unsent_reminders_and_the_review_request_move_to_the_new_start()
    {
        var (h, _, id) = await Resched.Confirmed(H25);
        await using var _ = h;
        Assert.Equal(HttpStatusCode.OK, (await Resched.Move(h, id, Life.Client, Resched.Wednesday10)).StatusCode);

        var start = DateTimeOffset.Parse(Resched.Wednesday10);
        var rows = await Resched.Rows(h, id);
        Assert.Equal(start.AddHours(-24), Assert.Single(rows, n => n.Kind == NotificationKind.Reminder24h).DueAt);
        Assert.Equal(start.AddHours(-1), Assert.Single(rows, n => n.Kind == NotificationKind.Reminder1h).DueAt);
        Assert.Equal(start.AddHours(1 + 2), Assert.Single(rows, n => n.Kind == NotificationKind.ReviewRequest).DueAt); // end + 2 h
        Assert.All(rows.Where(n => n.Kind != NotificationKind.Rescheduled), n => Assert.Equal(Life.Client, n.ToUserId));
    }

    [Fact]
    public async Task A_reminder_that_was_already_sent_stays_as_it_was()
    {
        var (h, _, id) = await Resched.Confirmed(H25);
        await using var _ = h;
        var sentAt = h.Clock.GetUtcNow().AddMinutes(-5);
        var oldDue = (await Resched.Rows(h, id)).Single(n => n.Kind == NotificationKind.Reminder24h).DueAt;
        await h.Seed(db => { var n = db.Notifications.Single(x => x.BookingId == id && x.Kind == NotificationKind.Reminder24h); n.SentAt = sentAt; n.Attempts = 1; });

        Assert.Equal(HttpStatusCode.OK, (await Resched.Move(h, id, Life.Client, Resched.Wednesday10)).StatusCode);

        var sent = Assert.Single(await Resched.Rows(h, id), n => n.Kind == NotificationKind.Reminder24h);
        Assert.Equal((oldDue, (DateTimeOffset?)sentAt, 1), (sent.DueAt, sent.SentAt, sent.Attempts));
    }

    [Fact]
    public async Task A_reminder_whose_new_time_has_passed_is_removed_not_sent_late()
    {
        var (h, _, id) = await Resched.Confirmed(H25);
        await using var _ = h;
        // Tuesday 08:00 UTC is 20 h after the clock: the 24 h reminder would be due in the past, the 1 h reminder is not.
        Assert.Equal(HttpStatusCode.OK, (await Resched.Move(h, id, Life.Client, "2026-11-17T08:00:00Z")).StatusCode);

        var rows = await Resched.Rows(h, id);
        Assert.DoesNotContain(rows, n => n.Kind == NotificationKind.Reminder24h);
        Assert.Equal(DateTimeOffset.Parse("2026-11-17T07:00:00Z"), Assert.Single(rows, n => n.Kind == NotificationKind.Reminder1h).DueAt);
        Assert.Single(rows, n => n.Kind == NotificationKind.ReviewRequest);
    }

    [Fact]
    public async Task A_refused_reschedule_writes_and_moves_no_notification()
    {
        var (h, _, id) = await Resched.Confirmed(TimeSpan.FromHours(23));
        await using var _ = h;
        var before = (await Resched.Rows(h, id)).OrderBy(n => n.Kind).Select(n => (n.Kind, n.DueAt)).ToList();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Resched.Move(h, id, Life.Client, Resched.Wednesday10)).StatusCode);

        Assert.Equal(before, (await Resched.Rows(h, id)).OrderBy(n => n.Kind).Select(n => (n.Kind, n.DueAt)).ToList());
    }

    [Fact]
    public void The_Rescheduled_mail_names_the_new_date_and_links_to_the_booking_and_carries_no_free_text()
    {
        var id = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var mail = NotificationTemplates.Render(NotificationKind.Rescheduled, "https://site.demo.invalid", id, new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.Zero));
        Assert.Contains("18.11.2026 11:00", mail.Text); // Warsaw time
        Assert.Contains("https://site.demo.invalid/moje/rezerwacje/" + id, mail.Text);
        Assert.Contains("zmieniony", mail.Subject);
    }
}
