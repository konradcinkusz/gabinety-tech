namespace Gabinety.Booking.Payments.Abstractions;

/// <summary>What the provider needs to open a checkout for one booking (ADR-0005: the platform never holds funds).</summary>
public sealed record CheckoutRequest(Guid BookingId, long AmountMinor, string Currency, string Description, string SuccessUrl);
public sealed record CheckoutResult(string ProviderPaymentId, string CheckoutUrl);
public sealed record RefundRequest(string ProviderPaymentId, long AmountMinor, string IdempotencyKey);
public sealed record RefundResult(string ProviderRefundId);
/// <param name="DestinationAccountId">The practitioner's connected account at the provider; null for a provider that needs none (the Mock).</param>
/// <param name="ProviderPaymentId">The booking's payment id at the provider (<c>payments.provider_payment_id</c>): the money paid out is tied to that charge.</param>
/// <param name="BookingId">The booking the payout is for; a provider may use it to group the money movements of one booking.</param>
public sealed record PayoutRequest(Guid PractitionerId, long AmountMinor, string Currency, string IdempotencyKey,
    string? DestinationAccountId = null, string? ProviderPaymentId = null, Guid? BookingId = null);
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
    /// <summary>True when a practitioner must own a ready connected account before a booking is taken and a payout made (every real vendor; not the Mock).</summary>
    bool RequiresPractitionerAccount => false;
    /// <summary>The request header that carries the webhook signature.</summary>
    string SignatureHeader => "X-Payment-Signature";
    /// <summary>True when the adapter itself holds the webhook secret (it refuses to start without one), so the host need not look for <c>Payments:WebhookSecret</c>.</summary>
    bool WebhookSecretConfigured => false;
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

public enum ConnectedAccountState { Pending, Ready }

/// <summary>The practitioner's account at the provider. <see cref="OnboardingUrl"/> is single use and short lived: it is never stored.</summary>
public sealed record ConnectedAccountStatus(string AccountId, ConnectedAccountState State);

/// <summary>
/// What a marketplace provider offers for practitioner onboarding (ADR proposal 0011). Implemented by the adapter next to <see cref="IPaymentProvider"/>;
/// a provider without it has no onboarding and the endpoints answer 404.
/// </summary>
public interface IConnectedAccountProvider
{
    Task<string> CreateAccountAsync(Guid practitionerId, CancellationToken ct);
    Task<string> CreateOnboardingLinkAsync(string accountId, string returnUrl, string refreshUrl, CancellationToken ct);
    Task<ConnectedAccountStatus> GetAccountStatusAsync(string accountId, CancellationToken ct);
}

/// <summary>A provider call that the provider answered with an error (4xx or 5xx). Carries the status and the provider's error code only, never a message that could hold user text.</summary>
public sealed class PaymentProviderException(int status, string? code) : Exception($"The payment provider answered HTTP {status}" + (code is null ? "" : $" ({code})"))
{
    public int Status { get; } = status;
    public string? Code { get; } = code;
}
