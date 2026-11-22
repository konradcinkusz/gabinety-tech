using System.Security.Cryptography;
using System.Text;
using Gabinety.Booking.Payments.Abstractions;
using Gabinety.Booking.Payments.Mock;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Gabinety.Booking.Tests;

public class MockPaymentProviderTests
{
    const string Secret = "whsec-test-DO-NOT-LEAK-1234";
    static readonly byte[] Body = Encoding.UTF8.GetBytes("""{"eventId":"evt_demo_1","kind":"PaymentSucceeded","providerPaymentId":"mock_abc","amountMinor":10000,"currency":"PLN"}""");

    static string Sign(byte[] body, string secret) => Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body)).ToLowerInvariant();

    [Fact]
    public async Task Checkout_url_is_site_base_url_checkout_mock_booking_id_and_the_payment_id_is_mock_guid_N()
    {
        var id = Guid.NewGuid();
        var r = await new MockPaymentProvider("https://demo.example.test", null).CreateCheckoutAsync(new(id, 10000, "PLN", "demo", "https://demo.example.test/ok"), default);
        Assert.Equal($"https://demo.example.test/checkout/mock/{id}", r.CheckoutUrl);
        Assert.Matches("^mock_[0-9a-f]{32}$", r.ProviderPaymentId);
    }

    [Fact]
    public async Task Refund_and_payout_return_deterministic_fake_ids_and_perform_nothing()
    {
        var p = new MockPaymentProvider("http://x", null);
        Assert.Equal("Mock", p.Name);
        var refund = await p.RefundAsync(new("mock_abc", 500, "key-1"), default);
        Assert.Equal(refund, await p.RefundAsync(new("mock_abc", 500, "key-1"), default));
        Assert.StartsWith("mockrefund_", refund.ProviderRefundId);
        var payout = await p.PayoutAsync(new(Guid.Parse("11111111-1111-1111-1111-111111111111"), 500, "PLN", "key-2"), default);
        Assert.Equal(payout, await p.PayoutAsync(new(Guid.Parse("11111111-1111-1111-1111-111111111111"), 500, "PLN", "key-2"), default));
        Assert.StartsWith("mockpayout_", payout.ProviderPayoutId);
    }

    [Fact]
    public void A_valid_signature_returns_the_event_and_an_invalid_or_missing_one_returns_null()
    {
        var p = new MockPaymentProvider("http://x", Secret);
        var e = p.VerifyWebhook(Body, Sign(Body, Secret));
        Assert.Equal(new WebhookEvent("evt_demo_1", WebhookEventKind.PaymentSucceeded, "mock_abc", 10000, "PLN"), e);
        Assert.Null(p.VerifyWebhook(Body, Sign(Body, "another-secret")));
        Assert.Null(p.VerifyWebhook(Body, "not-hex"));
        Assert.Null(p.VerifyWebhook(Body, null));
        Assert.Null(p.VerifyWebhook(Encoding.UTF8.GetBytes("""{"eventId":"tampered"}"""), Sign(Body, Secret)));
    }

    [Fact]
    public void Without_a_configured_secret_the_mock_accepts_any_signature()
    {
        var p = new MockPaymentProvider("http://x", null);
        Assert.Equal(WebhookEventKind.PaymentSucceeded, p.VerifyWebhook(Body, null)!.Kind);
        Assert.Equal(WebhookEventKind.PaymentSucceeded, p.VerifyWebhook(Body, "anything")!.Kind);
    }

    [Fact]
    public void An_unparsable_body_with_a_valid_signature_is_an_Unknown_event()
    {
        var raw = Encoding.UTF8.GetBytes("{not json");
        Assert.Equal(WebhookEventKind.Unknown, new MockPaymentProvider("http://x", Secret).VerifyWebhook(raw, Sign(raw, Secret))!.Kind);
    }

    /// <summary>
    /// The startup refusal is asserted on the feature itself (no WebApplicationFactory): a factory that refuses to build can surface ObjectDisposedException
    /// instead of the inner message, so a host-level assertion here would be a race. Host-level "does not start" is covered in MockPaymentsFailClosedTests.
    /// </summary>
    [Theory]
    [InlineData("Acme")]
    [InlineData("Paypal")]
    public void An_unknown_Payments_Provider_stops_the_host_at_startup_with_a_message_naming_the_key(string provider)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["GABINETY_FAKE"] = "1", ["Payments:Provider"] = provider });
        new PaymentsFeature().Add(builder);
        using var sp = builder.Services.BuildServiceProvider();

        var ex = Assert.Throws<InvalidOperationException>(() => sp.GetRequiredService<IPaymentProvider>());
        Assert.Contains("Payments:Provider", ex.Message);
        Assert.Contains(provider, ex.Message);
        Assert.Contains("adapter", ex.Message);
        // and the startup filter that makes the host refuse before it listens is registered
        Assert.Contains(builder.Services, d => d.ServiceType == typeof(IStartupFilter));
    }
}
