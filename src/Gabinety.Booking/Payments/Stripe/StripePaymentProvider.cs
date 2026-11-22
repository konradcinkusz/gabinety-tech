using System.Globalization;
using System.Text.Json;
using Gabinety.Booking.Payments.Abstractions;

namespace Gabinety.Booking.Payments.Stripe;

/// <summary>
/// <c>Payments:Provider=Stripe</c> (ADR proposal 0011). Stripe Connect, Express connected accounts, SEPARATE charges and transfers:
/// <list type="bullet">
/// <item><see cref="CreateCheckoutAsync"/>: a hosted Checkout Session in <c>payment</c> mode (card and BLIK, PLN). The charge is made on the platform; the session id is the provider payment id.</item>
/// <item><see cref="RefundAsync"/>: a refund of the session's PaymentIntent. It happens only before the money is transferred (the payout is not due until the dispute window has closed), so there is nothing to reverse.</item>
/// <item><see cref="PayoutAsync"/>: a Transfer to the practitioner's connected account, tied to the charge with <c>source_transaction</c> and <c>transfer_group</c>, for <c>price - fee</c> (CONTRACTS 9.1).
/// The fee is what stays on the platform side, so it is never moved and never has to be refunded.</item>
/// </list>
/// Every write has the caller's idempotency key. Network calls carry the caller's cancellation token (the 10 s budget of SPEC 5.1 is the caller's). No log line is written here.
/// </summary>
internal sealed class StripePaymentProvider(StripeApi api, string webhookSecret, TimeProvider time) : IPaymentProvider, IConnectedAccountProvider
{
    public const string ProviderName = "Stripe";
    /// <summary>Stripe's minimum for a Checkout Session's life is 30 minutes; the booking hold is 10, and a payment after the hold is refunded by the handler.</summary>
    static readonly TimeSpan SessionLife = TimeSpan.FromMinutes(35);
    const int MaxProductNameLength = 200;

    public string Name => ProviderName;
    public bool RequiresPractitionerAccount => true;
    public string SignatureHeader => "Stripe-Signature";
    public bool WebhookSecretConfigured => !string.IsNullOrEmpty(webhookSecret);

    public async Task<CheckoutResult> CreateCheckoutAsync(CheckoutRequest request, CancellationToken ct)
    {
        if (!string.Equals(request.Currency, "PLN", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Only PLN is supported.", nameof(request));
        if (request.AmountMinor <= 0) throw new ArgumentOutOfRangeException(nameof(request), "The amount must be positive.");
        var booking = request.BookingId.ToString("D");
        var name = request.Description.Length > MaxProductNameLength ? request.Description[..MaxProductNameLength] : request.Description;
        var form = new StripeForm()
            .Add("mode", "payment")
            .Add("success_url", request.SuccessUrl)
            .Add("cancel_url", request.SuccessUrl)
            .Add("client_reference_id", booking)
            .Add("locale", "pl")
            .Add("expires_at", (time.GetUtcNow() + SessionLife).ToUnixTimeSeconds())
            .Add("allowed_payment_method_types[0]", "card")
            .Add("allowed_payment_method_types[1]", "blik")
            .Add("line_items[0][quantity]", 1)
            .Add("line_items[0][price_data][currency]", "pln")
            .Add("line_items[0][price_data][unit_amount]", request.AmountMinor)
            .Add("line_items[0][price_data][product_data][name]", name)
            .Add("metadata[booking_id]", booking)
            .Add("payment_intent_data[transfer_group]", TransferGroup(request.BookingId))
            .Add("payment_intent_data[metadata][booking_id]", booking);
        var session = await api.PostAsync("/v1/checkout/sessions", form, $"checkout:{request.BookingId:N}", ct);
        var id = Required(session, "id");
        return new CheckoutResult(id, Required(session, "url"));
    }

    public async Task<RefundResult> RefundAsync(RefundRequest request, CancellationToken ct)
    {
        var intent = await PaymentIntentOfAsync(request.ProviderPaymentId, ct)
            ?? throw new InvalidOperationException("The checkout has no payment to refund yet.");
        return new RefundResult(await PostRefundAsync(intent, request.AmountMinor, request.ProviderPaymentId, request.IdempotencyKey, ct));
    }

    /// <summary>The refund call itself (the integration tier drives it against stripe-mock with ids of its own).</summary>
    internal async Task<string> PostRefundAsync(string paymentIntent, long amountMinor, string sourcePaymentId, string idempotencyKey, CancellationToken ct)
    {
        var form = new StripeForm()
            .Add("payment_intent", paymentIntent)
            .Add("amount", amountMinor)
            .Add($"metadata[{StripeEventMapper.RefundSourceMetadataKey}]", sourcePaymentId); // lets the refund webhook find the payment again
        return Required(await api.PostAsync("/v1/refunds", form, idempotencyKey, ct), "id");
    }

    public async Task<PayoutResult> PayoutAsync(PayoutRequest request, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(request.DestinationAccountId)) throw new InvalidOperationException("The practitioner has no connected account.");
        if (string.IsNullOrEmpty(request.ProviderPaymentId)) throw new InvalidOperationException("The payout has no payment to be paid from.");
        if (!string.Equals(request.Currency, "PLN", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Only PLN is supported.", nameof(request));
        var charge = await ChargeOfAsync(request.ProviderPaymentId, ct) ?? throw new InvalidOperationException("The payment has no charge to pay from.");
        var group = request.BookingId is { } booking ? TransferGroup(booking) : $"practitioner_{request.PractitionerId:N}";
        return new PayoutResult(await PostTransferAsync(request.DestinationAccountId, charge, request.AmountMinor, group, request.ProviderPaymentId, request.IdempotencyKey, ct));
    }

    /// <summary>The transfer call itself (the integration tier drives it against stripe-mock with ids of its own).</summary>
    internal async Task<string> PostTransferAsync(string destination, string sourceCharge, long amountMinor, string transferGroup, string sourcePaymentId, string idempotencyKey, CancellationToken ct)
    {
        var form = new StripeForm()
            .Add("amount", amountMinor)
            .Add("currency", "pln")
            .Add("destination", destination)
            .Add("source_transaction", sourceCharge)
            .Add("transfer_group", transferGroup)
            .Add($"metadata[{StripeEventMapper.RefundSourceMetadataKey}]", sourcePaymentId);
        return Required(await api.PostAsync("/v1/transfers", form, idempotencyKey, ct), "id");
    }

    public WebhookEvent? VerifyWebhook(ReadOnlySpan<byte> rawBody, string? signatureHeader) =>
        StripeWebhook.IsValid(rawBody, signatureHeader, webhookSecret, time.GetUtcNow()) ? StripeEventMapper.Map(rawBody) : null;

    // --- connected accounts (Express) ---

    public async Task<string> CreateAccountAsync(Guid practitionerId, CancellationToken ct)
    {
        var form = new StripeForm()
            .Add("controller[stripe_dashboard][type]", "express")
            .Add("controller[fees][payer]", "application")
            .Add("controller[losses][payments]", "application")
            .Add("controller[requirement_collection]", "stripe")
            .Add("country", "PL")
            .Add("capabilities[transfers][requested]", true)
            .Add("metadata[practitioner_id]", practitionerId.ToString("D"));
        return Required(await api.PostAsync("/v1/accounts", form, $"account:{practitionerId:N}", ct), "id");
    }

    public async Task<string> CreateOnboardingLinkAsync(string accountId, string returnUrl, string refreshUrl, CancellationToken ct)
    {
        var form = new StripeForm().Add("account", accountId).Add("type", "account_onboarding").Add("return_url", returnUrl).Add("refresh_url", refreshUrl);
        // a link is single use: every call is a new one, so the key is new as well
        return Required(await api.PostAsync("/v1/account_links", form, $"link:{accountId}:{Guid.NewGuid():N}", ct), "url");
    }

    public async Task<ConnectedAccountStatus> GetAccountStatusAsync(string accountId, CancellationToken ct)
    {
        var account = await api.GetAsync($"/v1/accounts/{Uri.EscapeDataString(accountId)}", null, ct);
        return new ConnectedAccountStatus(accountId, IsReady(account) ? ConnectedAccountState.Ready : ConnectedAccountState.Pending);
    }

    /// <summary>Ready to be paid: the details are in, payouts are on, and the <c>transfers</c> capability is active.</summary>
    internal static bool IsReady(JsonElement account) =>
        Bool(account, "details_submitted") && Bool(account, "payouts_enabled")
        && account.TryGetProperty("capabilities", out var caps) && caps.ValueKind == JsonValueKind.Object
        && caps.TryGetProperty("transfers", out var transfers) && transfers.ValueKind == JsonValueKind.String && transfers.GetString() == "active";

    // --- helpers ---

    static string TransferGroup(Guid bookingId) => $"booking_{bookingId:N}";

    /// <summary>The PaymentIntent of a paid session: null while the session has none. The session id is what Booking stores.</summary>
    async Task<string?> PaymentIntentOfAsync(string sessionId, CancellationToken ct)
    {
        var session = await api.GetAsync($"/v1/checkout/sessions/{Uri.EscapeDataString(sessionId)}", null, ct);
        if (!session.TryGetProperty("payment_intent", out var pi)) return null;
        return pi.ValueKind switch { JsonValueKind.String => pi.GetString(), JsonValueKind.Object => Str(pi, "id"), _ => null }; // an id, or the object when expanded
    }

    /// <summary>The charge behind a session's PaymentIntent (<c>latest_charge</c>), by expanding it in the one call.</summary>
    async Task<string?> ChargeOfAsync(string sessionId, CancellationToken ct)
    {
        var session = await api.GetAsync($"/v1/checkout/sessions/{Uri.EscapeDataString(sessionId)}", new StripeForm().Add("expand[]", "payment_intent"), ct);
        if (!session.TryGetProperty("payment_intent", out var pi) || pi.ValueKind != JsonValueKind.Object) return null;
        if (!pi.TryGetProperty("latest_charge", out var charge)) return null;
        return charge.ValueKind switch { JsonValueKind.String => charge.GetString(), JsonValueKind.Object => Str(charge, "id"), _ => null };
    }

    static string Required(JsonElement e, string name) =>
        Str(e, name) is { Length: > 0 } s ? s : throw new PaymentProviderException(200, "missing_" + name);

    static string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    static bool Bool(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}
