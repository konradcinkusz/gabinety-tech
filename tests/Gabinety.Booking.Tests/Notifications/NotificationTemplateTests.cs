using Gabinety.Booking.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Gabinety.Booking.Tests;

public class NotificationTemplateTests
{
    const string Base = "https://site.demo.invalid";
    static readonly Guid BookingId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    static readonly DateTimeOffset Start = new(2026, 12, 1, 9, 30, 0, TimeSpan.Zero); // 10:30 in Warsaw

    public static IEnumerable<object[]> Kinds => Enum.GetValues<NotificationKind>().Select(k => new object[] { k });

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Every_kind_has_a_Polish_text_and_html_version_with_an_absolute_link(NotificationKind kind)
    {
        var mail = NotificationTemplates.Render(kind, Base, BookingId, Start);
        Assert.False(string.IsNullOrWhiteSpace(mail.Subject));
        Assert.DoesNotContain('\n', mail.Subject);
        foreach (var body in new[] { mail.Text, mail.Html })
        {
            Assert.Contains(BookingId.ToString(), body);
            var link = LinksIn(body).Single();
            Assert.StartsWith(Base + "/", link);
        }
        Assert.Contains("<html", mail.Html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<", mail.Text);
        Assert.Matches("[ąćęłńóśźż]", mail.Subject + mail.Text); // Polish, not a placeholder
    }

    [Fact]
    public void Each_kind_has_its_own_subject()
    {
        var subjects = Enum.GetValues<NotificationKind>().Select(k => NotificationTemplates.Render(k, Base, BookingId, Start).Subject).ToList();
        Assert.Equal(subjects.Count, subjects.Distinct().Count());
    }

    [Fact]
    public void A_reminder_shows_the_start_in_Warsaw_time()
    {
        var mail = NotificationTemplates.Render(NotificationKind.Reminder24h, Base, BookingId, Start);
        Assert.Contains("01.12.2026 10:30", mail.Text);
        Assert.Contains("01.12.2026 10:30", mail.Html);
    }

    [Fact]
    public async Task A_trailing_slash_of_Site_BaseUrl_is_trimmed_in_every_link_the_dispatcher_sends()
    {
        using var t = new NotificationHost(setup: h => h.Settings["Site:BaseUrl"] = "https://site.demo.invalid/");
        var booking = await t.AddBooking();
        foreach (var kind in Enum.GetValues<NotificationKind>()) t.Add(booking.Id, kind, "demo-client");
        Assert.Equal(Enum.GetValues<NotificationKind>().Length, await t.Run());
        foreach (var m in t.Sender.Sent)
            foreach (var link in LinksIn(m.TextBody).Concat(LinksIn(m.HtmlBody)))
            {
                Assert.StartsWith("https://site.demo.invalid/", link);
                Assert.DoesNotContain("//", link["https://".Length..]);
            }
    }

    [Fact]
    public async Task No_template_contains_the_brief_or_any_other_free_text_of_the_booking()
    {
        const string marker = "MARKER-BRIEF-7f3a91";
        using var t = new NotificationHost();
        var booking = await t.AddBooking(edit: b =>
        {
            b.BriefText = marker + " moje dane osobowe";
            b.OfferingTitle = marker + "-title";
            b.DisputeText = marker + "-dispute";
            b.DisputeResolution = marker + "-resolution";
            b.StatusReason = marker + "-reason";
            b.MeetingUrlSnapshot = "https://meet.demo.invalid/" + marker;
        });
        foreach (var kind in Enum.GetValues<NotificationKind>()) t.Add(booking.Id, kind, "demo-client");

        Assert.Equal(Enum.GetValues<NotificationKind>().Length, await t.Run());
        Assert.Equal(Enum.GetValues<NotificationKind>().Length, t.Sender.Sent.Count);
        foreach (var m in t.Sender.Sent)
            Assert.DoesNotContain("MARKER", m.Subject + m.TextBody + m.HtmlBody);
    }

    static List<string> LinksIn(string body) =>
        System.Text.RegularExpressions.Regex.Matches(body, @"https?://[^\s""'<>]+").Select(m => m.Value).Distinct().ToList();
}
