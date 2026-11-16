namespace Gabinety.Booking.Payments.Abstractions;

/// <summary>What the provider needs to open a checkout for one booking (ADR-0005: the platform never holds funds).</summary>
public sealed record CheckoutRequest(Guid BookingId, long AmountMinor, string Currency, string Description, string SuccessUrl);
public sealed record CheckoutResult(string ProviderPaymentId, string CheckoutUrl);
public sealed record RefundRequest(string ProviderPaymentId, long AmountMinor, string IdempotencyKey);
public sealed record RefundResult(string ProviderRefundId);
public sealed record PayoutRequest(Guid PractitionerId, long AmountMinor, string Currency, string IdempotencyKey);
public sealed record PayoutResult(string ProviderPayoutId);

public enum WebhookEventKind { PaymentSucceeded, PaymentFailed, RefundSucceeded, Unknown }

public sealed record WebhookEvent(string EventId, WebhookEventKind Kind, string? ProviderPaymentId, long? AmountMinor, string? Currency);

/// <summary>
/// The payment abstraction (SPEC 6.2, P11). One adapter per vendor; <c>Mock</c> is the default and needs no credentials.
/// A real adapter makes network calls (10 s budget, SPEC 5.1): callers must never hold a database lock across one.
/// </summary>
public interface IPaymentProvider
{
    string Name { get; }
    Task<CheckoutResult> CreateCheckoutAsync(CheckoutRequest request, CancellationToken ct);
    Task<RefundResult> RefundAsync(RefundRequest request, CancellationToken ct);
    Task<PayoutResult> PayoutAsync(PayoutRequest request, CancellationToken ct);
    /// <summary>Null when the signature is invalid. A real adapter verifies over the RAW body in constant time.</summary>
    WebhookEvent? VerifyWebhook(ReadOnlySpan<byte> rawBody, string? signatureHeader);
}

/// <summary><c>Site:BaseUrl</c> (default http://localhost:3000) without a trailing slash.</summary>
public static class SiteUrl
{
    public static string From(Microsoft.Extensions.Configuration.IConfiguration config) =>
        (string.IsNullOrWhiteSpace(config["Site:BaseUrl"]) ? "http://localhost:3000" : config["Site:BaseUrl"]!).TrimEnd('/');
}
