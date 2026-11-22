using System.Net;
using Gabinety.Booking.Notifications;
using Gabinety.Booking.Notifications.Preferences;
using Gabinety.Booking.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gabinety.Booking.Tests;

/// <summary>T-11.12: what the dispatcher does with the recipient's preferences at send time (CONTRACTS 4.1, 8.1; DECISIONS 2026-11-22).</summary>
public class NotificationPreferencesDispatcherTests
{
    static Task Switch(NotificationHost t, string userId, NotificationKind kind, bool enabled) =>
        t.Host.Seed(db => db.NotificationPreferences.Add(new NotificationPreference { UserId = userId, Kind = kind, Enabled = enabled }));

    [Theory]
    [InlineData(NotificationKind.Reminder24h)]
    [InlineData(NotificationKind.Reminder1h)]
    [InlineData(NotificationKind.ReviewRequest)]
    public async Task A_switchable_kind_the_recipient_switched_off_is_not_sent_and_its_row_is_closed(NotificationKind kind)
    {
        using var t = new NotificationHost();
        var booking = await t.AddBooking(edit: b => { b.StartUtc = t.Now.AddDays(2); b.EndUtc = t.Now.AddDays(2).AddHours(1); });
        var n = t.Add(booking.Id, kind, "demo-client");
        await Switch(t, "demo-client", kind, enabled: false);

        Assert.Equal(0, await t.Run());
        t.Host.Clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(0, await t.Run());

        Assert.Empty(t.Sender.Sent);
        var row = await t.Row(n.Id);
        Assert.Equal((new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero), 0), (row.SentAt, row.Attempts)); // closed at the first cycle, never attempted
    }

    [Fact]
    public async Task A_change_made_after_the_row_was_scheduled_takes_effect_before_it_is_due()
    {
        using var t = new NotificationHost();
        var booking = await t.AddBooking();
        var n = t.Add(booking.Id, NotificationKind.Reminder24h, "demo-client", t.Now.AddHours(3));
        Assert.Equal(0, await t.Run()); // not due: still waiting
        await Switch(t, "demo-client", NotificationKind.Reminder24h, enabled: false);
        t.Host.Clock.Advance(TimeSpan.FromHours(3));

        await t.Run();

        Assert.Empty(t.Sender.Sent);
        Assert.NotNull((await t.Row(n.Id)).SentAt);
    }

    [Fact]
    public async Task An_explicit_enabled_row_and_a_switch_of_another_kind_or_another_user_do_not_stop_the_mail()
    {
        using var t = new NotificationHost();
        var booking = await t.AddBooking();
        t.Add(booking.Id, NotificationKind.Reminder24h, "demo-client");
        t.Add(booking.Id, NotificationKind.Reminder1h, "demo-client");
        t.Add(booking.Id, NotificationKind.Reminder24h, "demo-practitioner");
        await Switch(t, "demo-client", NotificationKind.Reminder24h, enabled: true);
        await Switch(t, "demo-client", NotificationKind.ReviewRequest, enabled: false);
        await Switch(t, "demo-practitioner", NotificationKind.Reminder1h, enabled: false);

        Assert.Equal(3, await t.Run());

        Assert.Equal(["Reminder1h", "Reminder24h"], t.Sender.For("demo-client").Select(m => m.Kind.ToString()).Order(StringComparer.Ordinal));
        Assert.Equal(NotificationKind.Reminder24h, Assert.Single(t.Sender.For("demo-practitioner")).Kind);
    }

    [Theory]
    [InlineData(NotificationKind.BookingConfirmed)]
    [InlineData(NotificationKind.Cancelled)]
    [InlineData(NotificationKind.DisputeOpened)]
    [InlineData(NotificationKind.DisputeResolved)]
    [InlineData(NotificationKind.PayoutSent)]
    [InlineData(NotificationKind.Rescheduled)]
    public async Task A_transactional_kind_is_sent_whatever_a_forged_preference_row_says(NotificationKind kind)
    {
        using var t = new NotificationHost();
        var booking = await t.AddBooking();
        var n = t.Add(booking.Id, kind, "demo-client");
        await Switch(t, "demo-client", kind, enabled: false); // the API cannot write this row; the dispatcher must not trust it

        Assert.Equal(1, await t.Run());

        Assert.Equal(kind, Assert.Single(t.Sender.For("demo-client")).Kind);
        Assert.NotNull((await t.Row(n.Id)).SentAt);
    }

    [Fact]
    public async Task After_a_reschedule_the_re_timed_reminder_that_is_switched_off_stays_unsent_and_the_others_still_go_out()
    {
        var sender = new RecordingEmailSender();
        var dbName = "prefs-" + Guid.NewGuid().ToString("N"); // an own database: the shared one lets another host's dispatcher claim these rows
        var (h, _, id) = await Resched.Confirmed(TimeSpan.FromHours(25), tweak: host => host.Configure += s =>
        {
            s.AddDbContext<BookingDbContext>(o => o.UseInMemoryDatabase(dbName));
            s.RemoveAll<IEmailSender>(); s.AddSingleton<IEmailSender>(sender);
            s.RemoveAll<IContactResolver>(); s.AddSingleton<IContactResolver>(new FakeContacts { Default = "demo-person@demo.invalid" });
        });
        await using var _ = h;
        await h.Seed(db => db.NotificationPreferences.Add(new NotificationPreference { UserId = Life.Client, Kind = NotificationKind.Reminder24h, Enabled = false }));
        Assert.Equal(HttpStatusCode.OK, (await Resched.Move(h, id, Life.Client, Resched.Wednesday10)).StatusCode);

        // Wednesday 10:00 UTC: the 24 h reminder became due on Tuesday 10:00, the 1 h reminder on Wednesday 09:00.
        h.Clock.Advance(TimeSpan.FromDays(2).Subtract(TimeSpan.FromHours(3)));
        var dispatcher = h.Services.GetRequiredService<NotificationDispatcher>();
        await dispatcher.RunOnceAsync(CancellationToken.None);

        var rows = await Resched.Rows(h, id);
        Assert.NotNull(Assert.Single(rows, n => n.Kind == NotificationKind.Reminder24h).SentAt); // closed, not delivered
        Assert.Equal(["Reminder1h", "Rescheduled"], sender.For(Life.Client).Select(m => m.Kind.ToString()).Order(StringComparer.Ordinal));
        Assert.Equal(["Rescheduled"], sender.For(Life.Practitioner).Select(m => m.Kind.ToString()));
        Assert.Null(Assert.Single(rows, n => n.Kind == NotificationKind.ReviewRequest).SentAt); // due after the end: still waiting
    }
}
