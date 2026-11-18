namespace Gabinety.Booking.Notifications.Smtp;

/// <summary>
/// The message was not accepted. The text carries the CLASS of the failure only (for example "SMTP 550", "timeout", "connection"), never an address, a subject,
/// a body, the host or the credentials, and the vendor exception is not kept as the inner exception, because its message can carry the recipient.
/// The dispatcher logs the type of this exception and counts the attempt as failed (SPEC 6.2: retried from the notifications table).
/// </summary>
public sealed class EmailDeliveryException(string message) : Exception(message);
