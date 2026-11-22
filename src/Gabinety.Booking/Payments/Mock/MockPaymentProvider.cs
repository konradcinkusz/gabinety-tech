using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Gabinety.Booking.Features.Internal.TestClock;
using Gabinety.Booking.Payments.Abstractions;
using Gabinety.ServiceDefaults;

namespace Gabinety.Booking.Payments.Mock;

/// <summary>
/// The fake profile's provider (<c>Payments:Provider=Mock</c>, refused outside it, F-01): the whole path works with zero credentials and moves no money.
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
            if (r.ValueKind != JsonValueKind.Object) return new WebhookEvent("", WebhookEventKind.Unknown, null, null, null);
            string? Str(string n) => r.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            var kind = Enum.TryParse<WebhookEventKind>(Str("kind"), out var k) ? k : WebhookEventKind.Unknown;
            long? amount = r.TryGetProperty("amountMinor", out var a) && a.ValueKind == JsonValueKind.Number && a.TryGetInt64(out var m) ? m : null;
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

/// <summary>
/// Registers the provider named by <c>Payments:Provider</c> (security review F-01: Mock is fail-closed).
/// <list type="bullet">
/// <item>Fake profile: <c>Mock</c> or no value is the Mock provider (<see cref="NonDeployedProfile"/>: <c>GABINETY_FAKE=1</c> or <c>Environment:Kind=Development</c>, the same guard as the clock and reset endpoints).</item>
/// <item>Any other profile: <c>Mock</c> or no value stops the host at startup. There is no default outside the fake profile.</item>
/// <item><c>Stripe</c> (T-09.05, ADR proposal 0011): the Stripe adapter, in every profile, when <c>Payments:Stripe:SecretKey</c> and <c>Payments:Stripe:WebhookSecret</c> are set; with either missing the host stops at startup.</item>
/// <item>Any other value: stops the host at startup, naming no vendor adapter.</item>
/// </list>
/// The refusal is raised when the host builds its pipeline, before it listens; a test of an unrelated concern in a deployed profile replaces <see cref="IPaymentProvider"/>
/// with its own double, which then never reaches this factory.
/// </summary>
public sealed class PaymentsFeature : IServiceFeature
{
    public const string StripeSecretKeyKey = "Payments:Stripe:SecretKey", StripeWebhookSecretKey = "Payments:Stripe:WebhookSecret";

    public void Add(IHostApplicationBuilder builder)
    {
        var provider = builder.Configuration["Payments:Provider"];
        var problem = Problem(provider, NonDeployedProfile.IsActive(builder.Configuration), builder.Configuration);
        var stripe = problem is null && string.Equals(provider, "Stripe", StringComparison.OrdinalIgnoreCase);
        if (stripe) builder.Services.AddHttpClient(StripeHttpClient, c =>
        {
            c.BaseAddress = new Uri(builder.Configuration["Payments:Stripe:ApiBase"] is { Length: > 0 } b ? b : "https://api.stripe.com");
            c.Timeout = TimeSpan.FromSeconds(15); // the callers' own 10 s budget (SPEC 5.1) normally cancels first
        });
        builder.Services.AddSingleton<IPaymentProvider>(sp =>
        {
            if (problem is not null) throw new InvalidOperationException(problem);
            var config = sp.GetRequiredService<IConfiguration>();
            if (stripe)
                return new Stripe.StripePaymentProvider(new Stripe.StripeApi(sp.GetRequiredService<IHttpClientFactory>().CreateClient(StripeHttpClient), config[StripeSecretKeyKey]!),
                    config[StripeWebhookSecretKey]!, sp.GetRequiredService<TimeProvider>());
            return new MockPaymentProvider(SiteUrl.From(config), config["Payments:WebhookSecret"]);
        });
        if (problem is not null) builder.Services.AddSingleton<IStartupFilter, ResolveProviderAtStartup>(); // refuse before the first request, and before the server listens
    }

    const string StripeHttpClient = "stripe";

    /// <summary>The reason the configuration is refused, or null. Never includes a configured value other than the provider name, never a secret.</summary>
    public static string? Problem(string? provider, bool nonDeployed, IConfiguration? config = null)
    {
        if (string.IsNullOrWhiteSpace(provider) || string.Equals(provider, "Mock", StringComparison.OrdinalIgnoreCase))
            return nonDeployed ? null
                : "Payments:Provider is 'Mock' or not set, and Mock payments run only in the fake profile (GABINETY_FAKE=1 or Environment:Kind=Development): " +
                  "anyone could mark a booking paid. Set Payments:Provider to the real provider (Stripe, T-09.05) with its secrets, docs/operations/PAYMENTS-GO-LIVE.md.";
        if (string.Equals(provider, "Stripe", StringComparison.OrdinalIgnoreCase))
        {
            var key = config?[StripeSecretKeyKey];
            var hook = config?[StripeWebhookSecretKey];
            if (string.IsNullOrWhiteSpace(key) || !(key.StartsWith("sk_", StringComparison.Ordinal) || key.StartsWith("rk_", StringComparison.Ordinal)))
                return "Payments:Provider is 'Stripe' but Payments:Stripe:SecretKey (secret Payments__Stripe__SecretKey) is not set or is not a Stripe secret or restricted key (sk_ or rk_).";
            if (string.IsNullOrWhiteSpace(hook) || !hook.StartsWith("whsec_", StringComparison.Ordinal))
                return "Payments:Provider is 'Stripe' but Payments:Stripe:WebhookSecret (secret Payments__Stripe__WebhookSecret) is not set or is not a Stripe webhook signing secret (whsec_): every webhook would be rejected.";
            return null;
        }
        return $"Payments:Provider '{provider}' has no adapter registered: the providers are Mock (fake profile only) and Stripe.";
    }

    public void Map(IEndpointRouteBuilder endpoints) { }

    sealed class ResolveProviderAtStartup : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            _ = app.ApplicationServices.GetRequiredService<IPaymentProvider>(); // throws the refusal
            next(app);
        };
    }
}
