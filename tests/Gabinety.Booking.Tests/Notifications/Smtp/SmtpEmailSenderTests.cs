using System.Diagnostics;
using Gabinety.Booking.Notifications;
using Gabinety.Booking.Notifications.Smtp;
using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;

namespace Gabinety.Booking.Tests.Smtp;

public class SmtpEmailSenderTests
{
    const string Address = "demo-client@demo.invalid";

    static SmtpOptions Options(FakeSmtpServer s, Action<SmtpOptions>? edit = null)
    {
        var o = new SmtpOptions
        {
            Host = "127.0.0.1", Port = s.Port, TlsMode = SmtpTlsMode.None, User = "demo-smtp-user", Password = "demo-smtp-password",
            FromAddress = "powiadomienia@demo.invalid", FromName = "Gabinety Tech", TimeoutSeconds = 5,
        };
        edit?.Invoke(o);
        return o;
    }

    static EmailMessage Message(string to = Address, string subject = "Rezerwacja potwierdzona", string text = "Twoja rezerwacja została potwierdzona.\n", string html = "<p>Twoja rezerwacja została potwierdzona.</p>") =>
        new(to, "demo-client", NotificationKind.BookingConfirmed, subject, text, html);

    static SmtpEmailSender Sender(SmtpOptions o) => new(o, TimeProvider.System, NullLogger<SmtpEmailSender>.Instance);

    static MimeMessage Parse(ReceivedMail m)
    {
        using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(m.Data));
        return MimeMessage.Load(ms);
    }

    [Fact]
    public async Task A_message_is_delivered_with_the_expected_envelope_and_headers()
    {
        using var server = new FakeSmtpServer();
        await Sender(Options(server)).SendAsync(Message(), CancellationToken.None);

        var mail = Assert.Single(server.Mails);
        Assert.Equal("<powiadomienia@demo.invalid>", mail.MailFrom);
        Assert.Equal([$"<{Address}>"], mail.RcptTo);
        Assert.Equal("demo-smtp-user", mail.AuthUser);
        Assert.Equal("demo-smtp-password", mail.AuthPassword);

        var parsed = Parse(mail);
        var from = Assert.Single(parsed.From.Mailboxes);
        Assert.Equal("powiadomienia@demo.invalid", from.Address);
        Assert.Equal("Gabinety Tech", from.Name);
        Assert.Equal(Address, Assert.Single(parsed.To.Mailboxes).Address);
        Assert.Equal("Rezerwacja potwierdzona", parsed.Subject);
        Assert.NotNull(parsed.MessageId);
        Assert.EndsWith("@demo.invalid>", "<" + parsed.MessageId + ">"); // the id's domain is the sender's own, not the machine name
        Assert.NotEqual(default, parsed.Date);
        Assert.Equal("auto-generated", parsed.Headers["Auto-Submitted"]);
        Assert.Empty(parsed.Cc); Assert.Empty(parsed.Bcc);
        Assert.Null(parsed.Headers["List-Unsubscribe"]); // not required: transactional (task text)
    }

    [Fact]
    public async Task The_message_is_multipart_alternative_with_a_plain_text_and_an_html_part_in_utf8()
    {
        using var server = new FakeSmtpServer();
        await Sender(Options(server)).SendAsync(Message(), CancellationToken.None);

        var parsed = Parse(Assert.Single(server.Mails));
        var alt = Assert.IsType<MultipartAlternative>(parsed.Body);
        var text = Assert.IsType<TextPart>(alt[0]);
        var html = Assert.IsType<TextPart>(alt[1]);
        Assert.True(text.IsPlain); Assert.True(html.IsHtml);
        Assert.Equal("utf-8", text.ContentType.Charset); Assert.Equal("utf-8", html.ContentType.Charset);
        Assert.Equal("Twoja rezerwacja została potwierdzona.\n", text.Text.Replace("\r\n", "\n"));
        Assert.Equal("<p>Twoja rezerwacja została potwierdzona.</p>", html.Text);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)] // a server without 8BITMIME: the sender must encode the body to 7 bit (quoted-printable or base64), not send raw UTF-8
    public async Task Polish_letters_survive_the_subject_and_both_parts_and_the_wire_is_7_bit_when_the_server_does_not_offer_8BITMIME(bool eightBit)
    {
        const string subject = "Przypomnienie: konsultacja już jutro — zażółć gęślą jaźń";
        const string body = "Zażółć gęślą jaźń ĄĘŁÓŻŹĆŃŚ\n";
        using var server = new FakeSmtpServer { Offer8BitMime = eightBit };
        await Sender(Options(server)).SendAsync(Message(subject: subject, text: body, html: "<p>" + body.Trim() + "</p>"), CancellationToken.None);

        var mail = Assert.Single(server.Mails);
        if (!eightBit) Assert.All(mail.Data, ch => Assert.True(ch < 128, "without 8BITMIME the wire form must be 7-bit, found U+" + ((int)ch).ToString("X4")));
        Assert.DoesNotContain("Zażółć", mail.Data.Split("\r\n\r\n")[0]); // headers are always RFC 2047 encoded, whatever the body does
        var parsed = Parse(mail);
        Assert.Equal(subject, parsed.Subject);
        Assert.Equal(body, parsed.TextBody!.Replace("\r\n", "\n"));
        Assert.Contains("zażółć", parsed.HtmlBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_subject_cannot_smuggle_a_header()
    {
        using var server = new FakeSmtpServer();
        await Sender(Options(server)).SendAsync(Message(subject: "Temat\r\nBcc: ofiara@demo.invalid"), CancellationToken.None);
        var mail = Assert.Single(server.Mails);
        var parsed = Parse(mail);
        Assert.Empty(parsed.Bcc);
        Assert.Null(parsed.Headers["Bcc"]);
        Assert.Equal([$"<{Address}>"], mail.RcptTo); // and no extra envelope recipient
    }

    [Theory]
    [InlineData("demo@demo.invalid\r\nBcc: ofiara@demo.invalid")]
    [InlineData("not-an-address")]
    [InlineData("")]
    public async Task A_malformed_recipient_is_refused_before_any_connection(string to)
    {
        using var server = new FakeSmtpServer();
        var ex = await Assert.ThrowsAsync<EmailDeliveryException>(() => Sender(Options(server)).SendAsync(Message(to: to), CancellationToken.None));
        Assert.Equal(0, server.Connections);
        Assert.DoesNotContain("ofiara", ex.ToString());
    }

    [Fact]
    public async Task A_provider_refusal_throws_and_neither_the_message_nor_the_inner_exceptions_carry_the_address()
    {
        using var server = new FakeSmtpServer { RejectRcpt = "demo-client" };
        var ex = await Assert.ThrowsAsync<EmailDeliveryException>(() => Sender(Options(server)).SendAsync(Message(), CancellationToken.None));
        Assert.Empty(server.Mails);
        Assert.DoesNotContain("demo-client", ex.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("demo.invalid", ex.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("demo-smtp-password", ex.ToString());
        Assert.Contains("550", ex.Message); // the class of failure is kept (a permanent refusal), the data is not
    }

    [Fact]
    public async Task An_unreachable_server_throws_a_delivery_exception()
    {
        int port; using (var s = new FakeSmtpServer()) port = s.Port; // closed again
        var o = new SmtpOptions { Host = "127.0.0.1", Port = port, TlsMode = SmtpTlsMode.None, FromAddress = "a@demo.invalid", TimeoutSeconds = 2 };
        await Assert.ThrowsAsync<EmailDeliveryException>(() => Sender(o).SendAsync(Message(), CancellationToken.None));
    }

    [Fact]
    public async Task A_stalled_server_is_given_up_after_the_timeout()
    {
        using var server = new FakeSmtpServer { Hang = true };
        var sw = Stopwatch.StartNew();
        await Assert.ThrowsAsync<EmailDeliveryException>(() => Sender(Options(server, o => o.TimeoutSeconds = 1)).SendAsync(Message(), CancellationToken.None));
        Assert.InRange(sw.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(8));
    }

    [Fact]
    public async Task Cancellation_is_not_a_delivery_failure()
    {
        using var server = new FakeSmtpServer { Hang = true };
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Sender(Options(server, o => o.TimeoutSeconds = 30)).SendAsync(Message(), cts.Token));
    }

    [Fact]
    public async Task Without_a_user_no_authentication_is_attempted()
    {
        using var server = new FakeSmtpServer();
        await Sender(Options(server, o => { o.User = null; o.Password = null; })).SendAsync(Message(), CancellationToken.None);
        Assert.Null(Assert.Single(server.Mails).AuthUser);
    }

    [Fact]
    public async Task The_sender_logs_neither_addresses_nor_subjects_nor_bodies_nor_the_password_on_success_or_failure()
    {
        var sink = new Gabinety.Booking.Tests.LogSink();
        using var factory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(sink));
        using var ok = new FakeSmtpServer();
        await new SmtpEmailSender(Options(ok), TimeProvider.System, factory.CreateLogger<SmtpEmailSender>())
            .SendAsync(Message(subject: "Tajny temat", text: "Tajna treść\n", html: "<p>Tajna treść</p>"), CancellationToken.None);
        using var refusing = new FakeSmtpServer { RejectRcpt = "demo-client" };
        await Assert.ThrowsAsync<EmailDeliveryException>(() => new SmtpEmailSender(Options(refusing), TimeProvider.System, factory.CreateLogger<SmtpEmailSender>())
            .SendAsync(Message(subject: "Tajny temat", text: "Tajna treść\n", html: "<p>Tajna treść</p>"), CancellationToken.None));

        var all = string.Join('\n', sink.Lines);
        Assert.NotEmpty(sink.Lines); // it does say something: which user, which kind
        Assert.Contains("demo-client", all.Replace(Address, "")); // the user id is allowed...
        foreach (var secret in new[] { Address, "demo.invalid", "Tajny temat", "Tajna treść", "demo-smtp-password", "demo-smtp-user", "127.0.0.1" })
            Assert.DoesNotContain(secret, all);
    }
}
