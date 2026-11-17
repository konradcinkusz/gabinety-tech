using Gabinety.Booking.Features.Bookings.Create;
using Gabinety.Booking.Features.Payments;
using Gabinety.Booking.Payments.Abstractions;
using Gabinety.Booking.Persistence;
using Gabinety.Contracts.Booking;

namespace Gabinety.Booking.Features.Lifecycle.Core;

/// <summary>Why money goes back. Part of the provider idempotency key, so one booking and one reason is one refund at the provider.</summary>
public enum RefundReason { ClientCancelledEarly, PractitionerCancelled }

/// <summary>A refund the provider accepted. <see cref="ConfirmedNow"/>: the provider confirms synchronously (the Mock), so the caller records it in its own transaction.</summary>
public sealed record RefundTicket(long AmountMinor, string IdempotencyKey, bool ConfirmedNow);

/// <summary>
/// Refunds in two steps so no lock is held across a network call (CONTRACTS 9.2):
/// <see cref="RequestAsync"/> asks the provider BEFORE the transition's transaction; <see cref="Record"/> runs inside it.
/// The ledger row is written when the provider confirms: the Mock confirms at once, so <see cref="Record"/> writes it; any other provider
/// leaves it to the <c>RefundSucceeded</c> webhook (<see cref="PaymentEventHandler"/>), which uses the same <see cref="RefundLedger"/> logic.
/// </summary>
public sealed class RefundCoordinator(IPaymentProvider provider)
{
    public static string IdempotencyKey(Guid bookingId, RefundReason reason) => $"refund:{bookingId:N}:{reason}";

    /// <summary>Asks the provider for <paramref name="amountMinor"/>. A repeated call with the same booking and reason is the same refund to the provider.</summary>
    public async Task<RefundTicket> RequestAsync(Guid bookingId, Payment payment, long amountMinor, RefundReason reason, CancellationToken ct)
    {
        if (payment.ProviderPaymentId is null || amountMinor <= 0 || amountMinor > payment.AmountMinor - payment.RefundedMinor)
            throw new InvalidOperationException($"Payment of booking {bookingId} cannot be refunded by {amountMinor}.");
        var key = IdempotencyKey(bookingId, reason);
        await provider.RefundAsync(new RefundRequest(payment.ProviderPaymentId, amountMinor, key), ct);
        return new RefundTicket(amountMinor, key, string.Equals(provider.Name, "Mock", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Inside the transaction: records the refund now when the provider already confirmed it, otherwise waits for the webhook.</summary>
    public static void Record(BookingTransitionContext context, RefundTicket ticket)
    {
        if (ticket.ConfirmedNow && context.Payment is { } payment)
            RefundLedger.Record(context.Db, context.Booking.Id, payment, ticket.AmountMinor, context.Now);
    }
}

/// <summary>The one idempotent way a confirmed refund is written: payment status and <c>refunded_minor</c>, and a ledger <c>Refund</c> row.</summary>
public static class RefundLedger
{
    /// <summary>Already refunded in full is a no-op (Ignored); more than the payment holds is Rejected; otherwise Applied.</summary>
    public static WebhookOutcome Record(BookingDbContext db, Guid bookingId, Payment payment, long? amountMinor, DateTimeOffset now)
    {
        if (amountMinor is not > 0 || payment.RefundedMinor >= payment.AmountMinor) return WebhookOutcome.Ignored;
        if (payment.RefundedMinor + amountMinor > payment.AmountMinor) return WebhookOutcome.Rejected;
        db.LedgerEntries.Add(new LedgerEntry { Id = Guid.CreateVersion7(), BookingId = bookingId, Kind = LedgerKind.Refund, AmountMinor = amountMinor.Value, CreatedAt = now });
        payment.RefundedMinor += amountMinor.Value;
        payment.Status = payment.RefundedMinor == payment.AmountMinor ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
        payment.UpdatedAt = now;
        return WebhookOutcome.Applied;
    }
}
