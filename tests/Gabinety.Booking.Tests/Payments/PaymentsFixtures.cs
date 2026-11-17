using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Gabinety.Booking.Payments.Abstractions;
using Gabinety.Booking.Payments.Mock;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gabinety.Booking.Tests;

/// <summary>Delegates to the mock and remembers every refund and checkout, so a test can see what was asked of the provider. <paramref name="name"/> "Mock" unless a test needs another mode.</summary>
sealed class RecordingProvider(string name = "Mock", string? secret = null) : IPaymentProvider
{
    readonly MockPaymentProvider _inner = new("http://localhost:3000", secret);
    public List<RefundRequest> Refunds { get; } = [];
    public string Name => name;
    public Task<CheckoutResult> CreateCheckoutAsync(CheckoutRequest r, CancellationToken ct) => _inner.CreateCheckoutAsync(r, ct);
    public Task<RefundResult> RefundAsync(RefundRequest r, CancellationToken ct) { lock (Refunds) Refunds.Add(r); return _inner.RefundAsync(r, ct); }
    public Task<PayoutResult> PayoutAsync(PayoutRequest r, CancellationToken ct) => _inner.PayoutAsync(r, ct);
    public WebhookEvent? VerifyWebhook(ReadOnlySpan<byte> rawBody, string? sig) => _inner.VerifyWebhook(rawBody, sig);
}

static class Pay
{
    public const string Secret = "whsec-test-DO-NOT-LEAK-1234";

    public static string Sign(string body, string secret = Secret) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body))).ToLowerInvariant();

    public static string EventJson(string eventId, string kind, string? providerPaymentId, long? amount = 10000, string currency = "PLN") =>
        System.Text.Json.JsonSerializer.Serialize(new { eventId, kind, providerPaymentId, amountMinor = amount, currency });

    public static async Task<HttpResponseMessage> Webhook(HttpClient c, string body, string? signature, string provider = "mock")
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/webhooks/payments/{provider}") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (signature != null) req.Headers.Add("X-Payment-Signature", signature);
        return await c.SendAsync(req);
    }

    public static Task<HttpResponseMessage> Simulate(HttpClient c, Guid bookingId, string outcome) =>
        c.PostAsJsonAsync("/api/v1/payments/simulate", new { bookingId, outcome });

    /// <summary>Creates a booking as <paramref name="client"/> for Tuesday 10:00 UTC and returns its id and provider payment id.</summary>
    public static async Task<(Guid Id, string ProviderPaymentId)> CreateBooking(CreateHost h, string client = "demo-client-1", string start = CreateHost.Tuesday10)
    {
        var r = await CreateHost.Post(h.ClientFor(client), h.Req(start), CreateHost.Key());
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var id = (await r.Body()).GetProperty("bookingId").GetGuid();
        return (id, await h.Query(db => db.Payments.Where(p => p.BookingId == id).Select(p => p.ProviderPaymentId!).SingleAsync()));
    }
}
