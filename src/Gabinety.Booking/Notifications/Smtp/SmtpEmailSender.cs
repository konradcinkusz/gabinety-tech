using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using MimeKit.Utils;

namespace Gabinety.Booking.Notifications.Smtp;

/// <summary>
/// <c>Notifications:Provider=Smtp</c>: delivers over standard SMTP (RFC 5321) with MailKit (MIT), so the e-mail vendor is a configuration choice, not a code
/// one (P11): every vendor type of this file stays inside <c>Gabinety.Booking.Notifications.Smtp</c> and none is public. One connection per message: the
/// volume is a few messages a minute, and a held connection would need keep-alive handling for nothing.
/// <para>PRIVACY: the sender logs the user id, the kind and the outcome class. Never the address, the subject, the body, the host or the credentials. The
/// exception it throws carries the failure class only (<see cref="EmailDeliveryException"/>).</para>
/// </summary>
public sealed class SmtpEmailSender(SmtpOptions options, TimeProvider time, ILogger<SmtpEmailSender> log) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        var mime = Build(message); // a malformed recipient fails here, before any connection

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        try
        {
            using var client = new SmtpClient { Timeout = options.TimeoutSeconds * 1000, CheckCertificateRevocation = true };
            await client.ConnectAsync(options.Host, options.Port, options.TlsMode switch
            {
                SmtpTlsMode.None => SecureSocketOptions.None,
                SmtpTlsMode.Implicit => SecureSocketOptions.SslOnConnect,
                _ => SecureSocketOptions.StartTls, // required, not "when available": a server that does not offer it fails the attempt
            }, timeout.Token);
            if (options.User is not null) await client.AuthenticateAsync(options.User, options.Password ?? "", timeout.Token);
            await client.SendAsync(mime, timeout.Token);
            await client.DisconnectAsync(quit: true, CancellationToken.None);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; } // the host is stopping: not a failed attempt of this message
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException) { throw Failed(message, "timeout"); }
        catch (SmtpCommandException ex) { throw Failed(message, $"SMTP {(int)ex.StatusCode}"); }
        catch (SmtpProtocolException) { throw Failed(message, "SMTP protocol"); }
        catch (AuthenticationException) { throw Failed(message, "authentication"); }
        catch (SslHandshakeException) { throw Failed(message, "TLS"); }
        catch (ServiceNotAuthenticatedException) { throw Failed(message, "authentication"); }
        catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException or MailKit.ProtocolException or ServiceNotConnectedException or InvalidOperationException)
        { throw Failed(message, "connection"); }

        log.LogInformation("Email to user {UserId}: kind {Kind} accepted by the provider", message.ToUserId, message.Kind);
    }

    EmailDeliveryException Failed(EmailMessage m, string reason)
    {
        log.LogWarning("Email to user {UserId}: kind {Kind} not accepted by the provider: {Reason}", m.ToUserId, m.Kind, reason);
        return new EmailDeliveryException("the provider did not accept the message: " + reason);
    }

    MimeMessage Build(EmailMessage m)
    {
        MailboxAddress to;
        try
        {
            if (!SmtpOptions.IsPlainAddress(m.ToAddress)) throw new FormatException();
            to = MailboxAddress.Parse(m.ToAddress);
        }
        catch (Exception ex) when (ex is FormatException or ParseException)
        {
            log.LogWarning("Email to user {UserId}: kind {Kind} not sent: the recipient address is malformed", m.ToUserId, m.Kind);
            throw new EmailDeliveryException("the recipient address is malformed");
        }

        var from = new MailboxAddress(options.FromName, options.FromAddress);
        var msg = new MimeMessage();
        msg.From.Add(from);
        msg.To.Add(to);
        msg.Subject = m.Subject.ReplaceLineEndings(" "); // headers are one line; MimeKit also encodes, this is the second belt
        msg.Date = time.GetUtcNow();
        msg.MessageId = MimeUtils.GenerateMessageId(from.Domain); // the sender's own domain, not the machine's name
        msg.Headers.Add("Auto-Submitted", "auto-generated"); // RFC 3834: no auto-replies to it
        msg.Headers.Add("Content-Language", "pl");
        var body = new BodyBuilder { TextBody = m.TextBody, HtmlBody = m.HtmlBody };
        msg.Body = body.ToMessageBody();
        return msg;
    }
}
