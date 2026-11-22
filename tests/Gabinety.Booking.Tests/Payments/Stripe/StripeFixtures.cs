using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Gabinety.Booking.Payments.Stripe;

namespace Gabinety.Booking.Tests.StripePayments;

/// <summary>Recorded-shape fixtures (built from stripe/openapi and Stripe's documented examples, see Fixtures/) and the doubles the Stripe tests share.</summary>
static class Fx
{
    public const string WebhookSecret = "whsec_test_DO-NOT-LEAK-0123456789abcdef";
    public const string SecretKey = "sk_test_DO-NOT-LEAK-0123456789abcdef";
    public const string SessionId = "cs_test_a1B2c3D4e5F6g7H8";
    public static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    public static string Dir => Path.Combine(AppContext.BaseDirectory, "Payments", "Stripe", "Fixtures");
    public static string Load(string name) => File.ReadAllText(Path.Combine(Dir, name + ".json"));
    public static byte[] Bytes(string name) => Encoding.UTF8.GetBytes(Load(name));

    /// <summary>The header Stripe sends: <c>t=...,v1=HMAC-SHA256(secret, "t.payload")</c>.</summary>
    public static string Sign(byte[] payload, DateTimeOffset at, string secret = WebhookSecret, string? scheme = "v1")
    {
        var t = at.ToUnixTimeSeconds().ToString();
        var mac = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(t + ".").Concat(payload).ToArray())).ToLowerInvariant();
        return $"t={t},{scheme}={mac}";
    }

    public static StripePaymentProvider Provider(Handler h, TimeProvider? time = null) =>
        new(new StripeApi(new HttpClient(h) { BaseAddress = new Uri("https://api.stripe.test") }, SecretKey), WebhookSecret, time ?? new FixedClock(Now));
}

sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

/// <summary>Answers by method and path from a table and records every request (headers, form body).</summary>
sealed class Handler : HttpMessageHandler
{
    public sealed record Seen(HttpMethod Method, string PathAndQuery, Dictionary<string, string> Headers, Dictionary<string, string> Form);
    public List<Seen> Requests { get; } = [];
    readonly Dictionary<string, (HttpStatusCode Status, string Body)> routes = [];

    public Handler On(HttpMethod m, string path, string body, HttpStatusCode status = HttpStatusCode.OK) { routes[$"{m} {path}"] = (status, body); return this; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var form = new Dictionary<string, string>();
        if (request.Content is not null)
            foreach (var pair in (await request.Content.ReadAsStringAsync(ct)).Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = pair.Split('=', 2);
                form[Uri.UnescapeDataString(kv[0].Replace('+', ' '))] = Uri.UnescapeDataString(kv[1].Replace('+', ' '));
            }
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        lock (Requests) Requests.Add(new(request.Method, request.RequestUri!.PathAndQuery, headers, form));
        var key = $"{request.Method} {request.RequestUri!.AbsolutePath}";
        if (!routes.TryGetValue(key, out var r)) return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{\"error\":{\"type\":\"invalid_request_error\"}}") };
        return new HttpResponseMessage(r.Status) { Content = new StringContent(r.Body, Encoding.UTF8, "application/json") };
    }
}
