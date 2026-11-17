namespace Gabinety.Booking.Notifications;

/// <summary>What a sender needs. <see cref="ToAddress"/> is personal data: it goes to the provider and nowhere else (never a log).</summary>
public sealed record EmailMessage(string ToAddress, string ToUserId, NotificationKind Kind, string Subject, string TextBody, string HtmlBody);

/// <summary>Extension point (P10). Throws when the message was not accepted; the dispatcher counts that as a failed attempt.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct);
}

/// <summary>The address of a user, asked from authservice by user id. Null when there is none (counts as a failed attempt).</summary>
public interface IContactResolver
{
    Task<string?> ResolveAddressAsync(string userId, CancellationToken ct);
}

/// <summary>
/// Default until the authservice user lookup contract is known (neither docs/spikes/authservice.md nor CONTRACTS section 1 gives an endpoint that
/// returns an address by user id): always null, so every notification is retried and finally given up. Replace it by registering another resolver.
/// </summary>
public sealed class NullContactResolver : IContactResolver
{
    public Task<string?> ResolveAddressAsync(string userId, CancellationToken ct) => Task.FromResult<string?>(null);
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
