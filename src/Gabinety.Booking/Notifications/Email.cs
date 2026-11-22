namespace Gabinety.Booking.Notifications;

/// <summary>What a sender needs. <see cref="ToAddress"/> is personal data: it goes to the provider and nowhere else (never a log).</summary>
public sealed record EmailMessage(string ToAddress, string ToUserId, NotificationKind Kind, string Subject, string TextBody, string HtmlBody);

/// <summary>Extension point (P10). Throws when the message was not accepted; the dispatcher counts that as a failed attempt.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct);
}

/// <summary>Who a notification is for, as far as an address lookup needs: the user, the side of the booking, and the client's e-mail snapshot from the booking row.</summary>
public sealed record ContactLookup(string UserId, bool IsPractitioner, string? ClientEmailSnapshot);

/// <summary>The address of a notification's recipient. Null when there is none (counts as a failed attempt, so the dispatcher retries). Must not throw for an outage.</summary>
public interface IContactResolver
{
    Task<string?> ResolveAddressAsync(ContactLookup recipient, CancellationToken ct);
}

/// <summary>Used only when <c>Backend:Directory</c> is not configured (no practitioner address can be found): always null, so every notification is retried and finally given up.</summary>
public sealed class NullContactResolver : IContactResolver
{
    public Task<string?> ResolveAddressAsync(ContactLookup recipient, CancellationToken ct) => Task.FromResult<string?>(null);
}

/// <summary><c>Notifications:Provider=Log</c> (default): logs the recipient USER id, the kind and the subject LENGTH. Never the address, never the body.</summary>
public sealed class LogEmailSender(ILogger<LogEmailSender> log) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        log.LogInformation("Email to user {UserId}: kind {Kind}, subject length {SubjectLength}", message.ToUserId, message.Kind, message.Subject.Length);
        return Task.CompletedTask;
    }
}
