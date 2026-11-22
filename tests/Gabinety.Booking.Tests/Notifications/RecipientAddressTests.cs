using System.Net;
using System.Net.Http.Json;
using Gabinety.Booking.Clients;
using Gabinety.Booking.Notifications;
using Gabinety.Booking.Persistence;
using Gabinety.Booking.Tests.Smtp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gabinety.Booking.Tests;

/// <summary>
/// T-04.09c, through the real host, the real dispatcher, the real resolver and the real SMTP sender (against the in-process listener): a booking made through the API
/// and confirmed through the payment simulation reaches the client at the address of the token that created it and the practitioner at the address Directory holds.
/// </summary>
public class RecipientAddressTests
{
    const string Client = "demo-client-1", ClientAddress = "demo-klient@demo.invalid", PractitionerAddress = "demo-praktyk@demo.invalid";
    const string Brief = "demo-brief: BARDZO-TAJNY-OPIS-PROBLEMU w moim planie", SmtpPassword = "demo-smtp-password-DO-NOT-LEAK";

    static MimeKit.MimeMessage Parse(ReceivedMail m)
    {
        using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(m.Data));
        return MimeKit.MimeMessage.Load(ms);
    }

    sealed class Rig : IDisposable
    {
        public readonly FakeSmtpServer Smtp = new();
        public readonly CreateHost Host = new();
        public string? PractitionerEmail = PractitionerAddress;
        public int ContactCalls;
        public NotificationDispatcher Dispatcher => Host.Services.GetRequiredService<NotificationDispatcher>();

        public Rig(bool smtp = true)
        {
            Host.Emails[Client] = ClientAddress;
            // Its OWN in-memory database: on the shared one another test's dispatcher would claim these rows (and this one theirs).
            var db = "recipients-" + Guid.NewGuid().ToString("N");
            Host.Configure = s => s.AddDbContext<BookingDbContext>(o => o.UseInMemoryDatabase(db));
            if (smtp)
            {
                Host.Settings["Notifications:Provider"] = "Smtp";
                Host.Settings["Notifications:Smtp:Host"] = "127.0.0.1";
                Host.Settings["Notifications:Smtp:Port"] = Smtp.Port.ToString();
                Host.Settings["Notifications:Smtp:TlsMode"] = "None";
                Host.Settings["Notifications:Smtp:User"] = "demo-smtp-user";
                Host.Settings["Notifications:Smtp:Password"] = SmtpPassword;
                Host.Settings["Notifications:Smtp:FromAddress"] = "powiadomienia@demo.invalid";
            }
            var offering = Host.Directory.Override!;
            Host.Directory.Override = (r, n) =>
            {
                if (r.RequestUri!.AbsolutePath != $"/internal/v1/practitioners/{Host.OwnerUserId}/contact") return offering(r, n);
                Interlocked.Increment(ref ContactCalls);
                return PractitionerEmail is null ? new HttpResponseMessage(HttpStatusCode.NotFound) : FakeDirectory.Json(new { email = PractitionerEmail });
            };
        }

        /// <summary>Creates a booking as the client with the secret brief and confirms it by the payment simulation: two confirmations, two reminders, a review request.</summary>
        public async Task<Guid> ConfirmedBooking()
        {
            await Host.WorkWeek();
            var r = await CreateHost.Post(Host.ClientFor(Client), Host.Req(text: Brief), CreateHost.Key());
            Assert.Equal(HttpStatusCode.Created, r.StatusCode);
            var id = (await r.Body()).GetProperty("bookingId").GetGuid();
            Assert.Equal(HttpStatusCode.OK, (await Pay.Simulate(Host.ClientFor(Client), id, "succeeded")).StatusCode);
            return id;
        }

        public Task<List<Notification>> Rows(Guid booking) => Host.Query(db => db.Notifications.AsNoTracking().Where(n => n.BookingId == booking).ToListAsync());
        public void Dispose() { Host.Dispose(); Smtp.Dispose(); }
    }

    [Fact]
    public async Task The_confirmation_reaches_the_client_at_the_token_address_and_the_practitioner_at_the_Directory_address()
    {
        using var t = new Rig();
        var id = await t.ConfirmedBooking();

        Assert.Equal(2, await t.Dispatcher.RunOnceAsync(default)); // only the two confirmations are due now

        var mails = t.Smtp.Mails;
        Assert.Equal(2, mails.Count);
        var toClient = Assert.Single(mails, m => m.RcptTo.SequenceEqual([$"<{ClientAddress}>"]));
        var toPractitioner = Assert.Single(mails, m => m.RcptTo.SequenceEqual([$"<{PractitionerAddress}>"]));
        Assert.Equal("Rezerwacja potwierdzona", Parse(toClient).Subject);
        Assert.Equal("Masz nową rezerwację", Parse(toPractitioner).Subject);
        Assert.All(mails, m => { Assert.DoesNotContain("BARDZO-TAJNY", Parse(m).TextBody); Assert.DoesNotContain("BARDZO-TAJNY", Parse(m).HtmlBody); });
        Assert.All(mails, m => { Assert.DoesNotContain("BARDZO-TAJNY", m.Data); Assert.DoesNotContain(Brief, m.Data); });
        Assert.All(await t.Rows(id), n => Assert.Equal(n.Kind == NotificationKind.BookingConfirmed ? 1 : 0, n.Attempts));
        Assert.Equal(FakeDirectory.Key, t.Host.Directory.LastKeyHeader);
        Assert.Equal(1, t.ContactCalls);
    }

    [Fact]
    public async Task Confirming_a_booking_by_payment_schedules_the_two_confirmations_the_reminder_still_ahead_and_the_review_request()
    {
        using var t = new Rig(smtp: false);
        var id = await t.ConfirmedBooking();
        var kinds = (await t.Rows(id)).Select(n => n.Kind).Order().ToList();
        Assert.Equal(new[] { NotificationKind.BookingConfirmed, NotificationKind.BookingConfirmed, NotificationKind.Reminder1h, NotificationKind.ReviewRequest }.Order(), kinds); // the start is 22 h away: no 24 h reminder
    }

    [Fact]
    public async Task A_Directory_outage_costs_the_practitioner_one_attempt_and_the_message_is_delivered_after_recovery_while_the_client_is_not_delayed()
    {
        using var t = new Rig();
        var id = await t.ConfirmedBooking();
        var contact = t.Host.Directory.Override!;
        t.Host.Directory.Override = (r, n) => r.RequestUri!.AbsolutePath.EndsWith("/contact") ? throw new HttpRequestException("demo directory down") : contact(r, n);

        Assert.Equal(1, await t.Dispatcher.RunOnceAsync(default)); // the client's confirmation only; the cycle does not throw
        var rows = await t.Rows(id);
        var pending = Assert.Single(rows, n => n.Kind == NotificationKind.BookingConfirmed && n.SentAt is null);
        Assert.Equal(1, pending.Attempts);
        Assert.Single(t.Smtp.Mails);

        t.Host.Directory.Override = contact;
        t.Host.Clock.Advance(NotificationDispatcher.Backoff[0]);
        Assert.Equal(1, await t.Dispatcher.RunOnceAsync(default));
        Assert.Equal(2, t.Smtp.Mails.Count);
        Assert.Contains(t.Smtp.Mails, m => m.RcptTo.SequenceEqual([$"<{PractitionerAddress}>"]));
    }

    [Fact]
    public async Task A_practitioner_without_a_held_address_is_retried_then_given_up_and_never_sent_anywhere_else()
    {
        using var t = new Rig { PractitionerEmail = null };
        var id = await t.ConfirmedBooking();
        for (var i = 0; i < NotificationDispatcher.MaxAttempts + 1; i++)
        {
            await t.Dispatcher.RunOnceAsync(default);
            t.Host.Clock.Advance(TimeSpan.FromHours(17));
        }
        var row = Assert.Single(await t.Rows(id), n => n.Kind == NotificationKind.BookingConfirmed && n.ToUserId == t.Host.OwnerUserId);
        Assert.Equal((null, NotificationDispatcher.MaxAttempts), (row.SentAt, row.Attempts));
        Assert.All(t.Smtp.Mails, m => Assert.DoesNotContain(m.RcptTo, r => r.Contains("demo-praktyk")));
    }

    [Fact]
    public async Task A_booking_without_a_client_address_is_given_up_for_the_client_but_the_practitioner_is_still_notified()
    {
        using var t = new Rig();
        t.Host.Emails[Client] = ""; // a token with no email claim
        var id = await t.ConfirmedBooking();
        await t.Dispatcher.RunOnceAsync(default);
        var mails = t.Smtp.Mails;
        Assert.Single(mails);
        Assert.Equal([$"<{PractitionerAddress}>"], mails[0].RcptTo);
        Assert.Null((await t.Rows(id)).Single(n => n.Kind == NotificationKind.BookingConfirmed && n.ToUserId == Client).SentAt);
    }

    [Fact]
    public async Task No_log_line_of_the_whole_flow_carries_an_address_the_brief_or_the_smtp_password()
    {
        using var t = new Rig();
        await t.ConfirmedBooking();
        await t.Dispatcher.RunOnceAsync(default);
        t.Host.Directory.Override = (_, _) => throw new HttpRequestException("demo directory down");
        t.Host.Clock.Advance(TimeSpan.FromDays(2));
        await t.Dispatcher.RunOnceAsync(default);

        Assert.NotEmpty(t.Host.Logs.Lines);
        foreach (var line in t.Host.Logs.Lines)
        {
            Assert.DoesNotContain(ClientAddress, line);
            Assert.DoesNotContain(PractitionerAddress, line);
            Assert.DoesNotContain("demo-klient", line);
            Assert.DoesNotContain("demo-praktyk", line);
            Assert.DoesNotContain("TAJNY", line);
            Assert.DoesNotContain(SmtpPassword, line);
            Assert.DoesNotContain(FakeDirectory.Key, line);
        }
    }

    [Fact]
    public async Task In_the_fake_profile_the_resolver_is_real_the_sender_is_the_log_sender_and_the_notification_is_delivered_to_the_log()
    {
        using var t = new Rig(smtp: false); // GABINETY_FAKE=1, Notifications:Provider unset
        Assert.IsType<ContactResolver>(t.Host.Services.GetRequiredService<IContactResolver>());
        Assert.IsType<LogEmailSender>(t.Host.Services.GetRequiredService<IEmailSender>());
        var id = await t.ConfirmedBooking();

        Assert.Equal(2, await t.Dispatcher.RunOnceAsync(default));

        Assert.Empty(t.Smtp.Mails);
        Assert.All((await t.Rows(id)).Where(n => n.Kind == NotificationKind.BookingConfirmed), n => Assert.NotNull(n.SentAt));
        var lines = t.Host.Logs.Lines.Where(l => l.StartsWith("Gabinety.Booking.Notifications.LogEmailSender")).ToList();
        Assert.Equal(2, lines.Count);
        Assert.All(t.Host.Logs.Lines, l => { Assert.DoesNotContain(ClientAddress, l); Assert.DoesNotContain(PractitionerAddress, l); });
    }

    [Fact]
    public void Without_Backend_Directory_the_null_resolver_stays_and_with_it_the_real_one_is_registered()
    {
        using var without = new CreateHost();
        without.Settings["Backend:Directory"] = "";
        Assert.IsType<NullContactResolver>(without.Services.GetRequiredService<IContactResolver>());

        using var with = new CreateHost(); // Backend:Directory is set by the fixture, as the fake profile and a deployed one set it
        Assert.IsType<ContactResolver>(with.Services.GetRequiredService<IContactResolver>());
        Assert.IsType<DirectoryClient>(with.Services.GetRequiredService<IDirectoryClient>());
    }
}
