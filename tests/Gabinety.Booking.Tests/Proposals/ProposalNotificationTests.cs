using System.Net;
using Gabinety.Booking.Features.Proposals;
using Gabinety.Booking.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace Gabinety.Booking.Tests;

/// <summary>ADR proposal 0013 section 4: templates without any user text, and rows that let every round of a negotiation send its own e-mail.</summary>
public class ProposalNotificationTests
{
    const string Base = "https://site.demo.invalid";
    static readonly Guid Id = Guid.Parse("aaaaaaaa-2222-3333-4444-555555555555");
    static readonly NotificationKind[] ProposalKinds =
        [NotificationKind.ProposalReceived, NotificationKind.ProposalCountered, NotificationKind.ProposalAccepted, NotificationKind.ProposalRejected, NotificationKind.ProposalExpired];

    public static IEnumerable<object[]> Cases => ProposalKinds.SelectMany(k => new[] { new object[] { k, true }, new object[] { k, false } });

    [Fact]
    public void The_proposal_kinds_are_exactly_the_ones_the_template_knows() =>
        Assert.Equal(ProposalKinds.Order(), Enum.GetValues<NotificationKind>().Where(NotificationTemplates.IsProposalKind).Order());

    [Theory, MemberData(nameof(Cases))]
    public void Every_proposal_kind_has_a_Polish_mail_with_one_link_to_the_thread_in_the_recipients_own_area(NotificationKind kind, bool forPractitioner)
    {
        var mail = NotificationTemplates.RenderProposal(kind, Base, Id, forPractitioner);
        var path = forPractitioner ? $"/gabinet-programisty/propozycje/{Id}" : $"/moje/propozycje/{Id}";
        Assert.False(string.IsNullOrWhiteSpace(mail.Subject));
        Assert.DoesNotContain('\n', mail.Subject);
        foreach (var body in new[] { mail.Text, mail.Html })
        {
            var links = System.Text.RegularExpressions.Regex.Matches(body, @"https?://[^\s""'<>]+").Select(m => m.Value).Distinct().ToList();
            Assert.Equal(Base + path, Assert.Single(links));
        }
        Assert.DoesNotContain("<", mail.Text);
        Assert.Contains("<html", mail.Html, StringComparison.OrdinalIgnoreCase);
        Assert.Matches("[ąćęłńóśźż]", mail.Subject + mail.Text);
        Assert.DoesNotContain("Termin", mail.Text); // a proposal has no start time of its own
    }

    [Fact]
    public void Each_proposal_kind_has_its_own_subject_and_a_trailing_slash_is_trimmed()
    {
        var subjects = ProposalKinds.Select(k => NotificationTemplates.RenderProposal(k, Base, Id, true).Subject).ToList();
        Assert.Equal(subjects.Count, subjects.Distinct().Count());
        Assert.Contains($"{Base}/moje/propozycje/{Id}", NotificationTemplates.RenderProposal(NotificationKind.ProposalReceived, Base + "/", Id, false).Text);
    }

    [Fact]
    public void A_booking_template_refuses_a_proposal_kind_and_the_other_way_round()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NotificationTemplates.Render(NotificationKind.ProposalReceived, Base, Id, DateTimeOffset.UnixEpoch));
        Assert.Throws<ArgumentOutOfRangeException>(() => NotificationTemplates.RenderProposal(NotificationKind.BookingConfirmed, Base, Id, true));
    }

    [Fact]
    public async Task A_proposal_row_is_scheduled_once_per_proposal_kind_user_and_round()
    {
        using var t = new NotificationHost();
        await using var h = new ProposalHost(t.Host);
        var id = await h.Create();
        var p = await h.Row(id);
        await t.Host.Seed(db =>
        {
            var writer = new NotificationWriter(db);
            Assert.True(writer.ScheduleProposal(p, NotificationKind.ProposalCountered, "demo-user-x", 2, t.Now));
            Assert.False(writer.ScheduleProposal(p, NotificationKind.ProposalCountered, "demo-user-x", 2, t.Now)); // the same round
            Assert.True(writer.ScheduleProposal(p, NotificationKind.ProposalCountered, "demo-user-x", 3, t.Now)); // the next round
            Assert.True(writer.ScheduleProposal(p, NotificationKind.ProposalCountered, "demo-user-y", 2, t.Now)); // another recipient
        });
        Assert.Equal(1 + 3, (await h.Notifications(id)).Count); // the first one is the creation's own
    }

    [Fact]
    public async Task The_dispatcher_sends_a_mail_to_each_party_for_each_round_with_a_link_and_no_text()
    {
        const string marker = "MARKER-MAIL-5e1f";
        using var t = new NotificationHost();
        t.Contacts.Addresses[ProposalHost.Client] = "client@demo.invalid";
        await using var h = new ProposalHost(t.Host);
        t.Contacts.Addresses[h.PractitionerUser] = "practitioner@demo.invalid";

        var id = await h.Create(body: h.Req(text: marker + " szukamy programisty do współpracy nad systemem.", company: marker + "-firma"));
        Assert.Equal(1, await t.Run());
        var received = Assert.Single(t.Sender.For(h.PractitionerUser));
        Assert.Equal("practitioner@demo.invalid", received.ToAddress);
        Assert.Contains($"{t.Host.Services.GetRequiredService<IConfiguration>()["Site:BaseUrl"] ?? "http://localhost:3000"}/gabinet-programisty/propozycje/{id}", received.TextBody);

        // three counters in a row: three more mails, none lost to the unique key
        await ProposalHost.Counter(h.AsPractitioner(), id, 1, text: marker + " inne warunki");
        await ProposalHost.Counter(h.AsClient(), id, 2, text: marker + " kolejne warunki");
        await ProposalHost.Counter(h.AsPractitioner(), id, 3);
        Assert.Equal(3, await t.Run());
        Assert.Equal(2, t.Sender.For(ProposalHost.Client).Count);
        Assert.Equal(2, t.Sender.For(h.PractitionerUser).Count);
        Assert.All(t.Sender.Sent, m => Assert.DoesNotContain("MARKER", m.Subject + m.TextBody + m.HtmlBody));

        // an acceptance tells both
        await ProposalHost.Act(h.AsClient(), id, "accept", new { round = 4 });
        Assert.Equal(2, await t.Run());
        Assert.Equal(3, t.Sender.For(ProposalHost.Client).Count);
        Assert.Equal(3, t.Sender.For(h.PractitionerUser).Count);
        Assert.Contains($"/moje/propozycje/{id}", t.Sender.For(ProposalHost.Client).Last().TextBody);
        Assert.Contains($"/gabinet-programisty/propozycje/{id}", t.Sender.For(h.PractitionerUser).Last().TextBody);
    }

    [Fact]
    public async Task A_mail_that_could_not_be_sent_is_retried_and_a_missing_proposal_is_a_failed_attempt()
    {
        using var t = new NotificationHost();
        t.Contacts.Addresses.Clear();
        t.Contacts.Default = null; // nobody has an address yet
        await using var h = new ProposalHost(t.Host);
        var id = await h.Create();
        Assert.Equal(0, await t.Run());
        var n = Assert.Single(await h.Notifications(id));
        Assert.Equal(1, n.Attempts);
        Assert.Null(n.SentAt);
        Assert.True(n.DueAt > t.Now); // the backoff moved it

        t.Contacts.Default = "demo-person@demo.invalid";
        t.Host.Clock.Advance(TimeSpan.FromMinutes(11));
        Assert.Equal(1, await t.Run());
        Assert.NotNull((await h.Notifications(id)).Single().SentAt);

        // a row whose proposal is gone is a failed attempt, never an exception
        await t.Host.Seed(db => db.Notifications.Add(new Notification { Id = Guid.NewGuid(), ProposalId = Guid.NewGuid(), Kind = NotificationKind.ProposalReceived, ToUserId = "demo-user-z", DueAt = t.Now, Round = 1 }));
        Assert.Equal(0, await t.Run());
    }
}
