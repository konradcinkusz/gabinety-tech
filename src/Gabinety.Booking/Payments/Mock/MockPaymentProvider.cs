using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Gabinety.Booking.Payments.Abstractions;
using Gabinety.ServiceDefaults;

namespace Gabinety.Booking.Payments.Mock;

/// <summary>
/// The default provider (<c>Payments:Provider=Mock</c>): the whole path works with zero credentials and moves no money.
/// The webhook is verified with an HMAC-SHA256 hex over the raw body when <c>Payments:WebhookSecret</c> is set; with no secret the mock accepts anything.
/// </summary>
public sealed class MockPaymentProvider(string siteBaseUrl, string? webhookSecret) : IPaymentProvider
{
    public string Name => "Mock";

    public Task<CheckoutResult> CreateCheckoutAsync(CheckoutRequest request, CancellationToken ct) =>
        Task.FromResult(new CheckoutResult($"mock_{Guid.NewGuid():N}", $"{siteBaseUrl.TrimEnd('/')}/checkout/mock/{request.BookingId}"));

    public Task<RefundResult> RefundAsync(RefundRequest request, CancellationToken ct) =>
        Task.FromResult(new RefundResult($"mockrefund_{request.IdempotencyKey}"));

    public Task<PayoutResult> PayoutAsync(PayoutRequest request, CancellationToken ct) =>
        Task.FromResult(new PayoutResult($"mockpayout_{request.IdempotencyKey}"));

    public WebhookEvent? VerifyWebhook(ReadOnlySpan<byte> rawBody, string? signatureHeader)
    {
        if (!string.IsNullOrEmpty(webhookSecret) && !SignatureMatches(rawBody, signatureHeader)) return null;
        try
        {
            using var doc = JsonDocument.Parse(rawBody.ToArray());
            var r = doc.RootElement;
            string? Str(string n) => r.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            var kind = Enum.TryParse<WebhookEventKind>(Str("kind"), out var k) ? k : WebhookEventKind.Unknown;
            long? amount = r.TryGetProperty("amountMinor", out var a) && a.TryGetInt64(out var m) ? m : null;
            return new WebhookEvent(Str("eventId") ?? "", kind, Str("providerPaymentId"), amount, Str("currency"));
        }
        catch (JsonException) { return new WebhookEvent("", WebhookEventKind.Unknown, null, null, null); }
    }

    bool SignatureMatches(ReadOnlySpan<byte> rawBody, string? header)
    {
        if (string.IsNullOrWhiteSpace(header)) return false;
        byte[] given;
        try { given = Convert.FromHexString(header.Trim()); } catch (FormatException) { return false; }
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(webhookSecret!), rawBody);
        return CryptographicOperations.FixedTimeEquals(given, expected); // constant time over the raw body
    }
}

/// <summary>Registers the provider named by <c>Payments:Provider</c>. Only <c>Mock</c> exists; any other value stops the host at startup.</summary>
public sealed class PaymentsFeature : IServiceFeature
{
    public void Add(IHostApplicationBuilder builder)
    {
        var provider = builder.Configuration["Payments:Provider"];
        if (!string.IsNullOrWhiteSpace(provider) && !string.Equals(provider, "Mock", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Payments:Provider must be 'Mock' (the only provider registered so far).");
        builder.Services.AddSingleton<IPaymentProvider>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            return new MockPaymentProvider(SiteUrl.From(config), config["Payments:WebhookSecret"]);
        });
    }

    public void Map(IEndpointRouteBuilder endpoints) { }
}
