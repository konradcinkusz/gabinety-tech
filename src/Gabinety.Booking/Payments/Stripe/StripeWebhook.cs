using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Gabinety.Booking.Payments.Abstractions;

namespace Gabinety.Booking.Payments.Stripe;

/// <summary>
/// The documented <c>Stripe-Signature</c> scheme: the header is <c>t=&lt;unix seconds&gt;,v1=&lt;hex&gt;[,v1=&lt;hex&gt;][,v0=...]</c>; each <c>v1</c> is HMAC-SHA256, key = the endpoint secret
/// (the whole <c>whsec_...</c> string as UTF-8), over <c>"{t}." + raw body</c>. Verified over the RAW bytes, in constant time, with a 5 minute tolerance, before anything is parsed.
/// More than one <c>v1</c> is normal while a secret is being rolled: any match passes.
/// </summary>
internal static class StripeWebhook
{
    public static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

    public static bool IsValid(ReadOnlySpan<byte> rawBody, string? header, string secret, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(header) || string.IsNullOrEmpty(secret)) return false;
        long? timestamp = null;
        List<byte[]> candidates = [];
        foreach (var part in header.Split(','))
        {
            var kv = part.Trim().Split('=', 2);
            if (kv.Length != 2) continue;
            if (kv[0] == "t" && long.TryParse(kv[1], NumberStyles.None, CultureInfo.InvariantCulture, out var t)) timestamp = t;
            else if (kv[0] == "v1" && kv[1].Length == 64 && TryHex(kv[1], out var sig)) candidates.Add(sig);
        }
        if (timestamp is not { } ts || candidates.Count == 0) return false;
        // outside the tolerance in either direction (a replay, or a clock far off) is refused; the arithmetic cannot overflow for a parsed long
        var age = now.ToUnixTimeSeconds() - ts;
        if (age > (long)Tolerance.TotalSeconds || age < -(long)Tolerance.TotalSeconds) return false;

        var prefix = Encoding.UTF8.GetBytes(ts.ToString(CultureInfo.InvariantCulture) + ".");
        var signed = new byte[prefix.Length + rawBody.Length];
        prefix.CopyTo(signed, 0);
        rawBody.CopyTo(signed.AsSpan(prefix.Length));
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), signed);
        var ok = false;
        foreach (var c in candidates) ok |= CryptographicOperations.FixedTimeEquals(c, expected); // no early exit: the time does not say which candidate matched
        return ok;
    }

    static bool TryHex(string s, out byte[] bytes)
    {
        try { bytes = Convert.FromHexString(s); return true; }
        catch (FormatException) { bytes = []; return false; }
    }
}

/// <summary>
/// Stripe events to the internal vocabulary, at the boundary (P11). Booking identifies a payment by the Checkout Session id (<c>cs_...</c>), which is also what it stores as the
/// provider payment id. Events used (names from the OpenAPI spec): <c>checkout.session.completed</c> (paid only), <c>checkout.session.async_payment_succeeded</c> (BLIK and other
/// delayed methods), <c>checkout.session.async_payment_failed</c>, <c>checkout.session.expired</c>, <c>refund.created</c> and <c>refund.updated</c> with status <c>succeeded</c>.
/// Everything else, and everything unreadable, is <see cref="WebhookEventKind.Unknown"/>: the handler records it as Ignored.
/// </summary>
internal static class StripeEventMapper
{
    public const string RefundSourceMetadataKey = "provider_payment_id";

    public static WebhookEvent Map(ReadOnlySpan<byte> rawBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawBody.ToArray());
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Unknown("");
            var id = Str(root, "id") ?? "";
            var type = Str(root, "type");
            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object
                || !data.TryGetProperty("object", out var obj) || obj.ValueKind != JsonValueKind.Object) return Unknown(id);

            switch (type)
            {
                case "checkout.session.completed" when Str(obj, "payment_status") == "paid":
                case "checkout.session.async_payment_succeeded":
                    return new WebhookEvent(id, WebhookEventKind.PaymentSucceeded, Str(obj, "id"), Long(obj, "amount_total"), Upper(Str(obj, "currency")));
                case "checkout.session.async_payment_failed":
                case "checkout.session.expired":
                    return new WebhookEvent(id, WebhookEventKind.PaymentFailed, Str(obj, "id"), Long(obj, "amount_total"), Upper(Str(obj, "currency")));
                case "refund.created" or "refund.updated" when Str(obj, "status") == "succeeded":
                    // a refund Booking asked for carries the payment id it was made for (RefundAsync); one made in the Dashboard does not and is not Booking's to record
                    if (obj.TryGetProperty("metadata", out var md) && md.ValueKind == JsonValueKind.Object && Str(md, RefundSourceMetadataKey) is { Length: > 0 } paymentId)
                        return new WebhookEvent(id, WebhookEventKind.RefundSucceeded, paymentId, Long(obj, "amount"), Upper(Str(obj, "currency")));
                    return Unknown(id);
                default:
                    return Unknown(id);
            }
        }
        catch (JsonException) { return Unknown(""); }
    }

    static WebhookEvent Unknown(string id) => new(id, WebhookEventKind.Unknown, null, null, null);
    static string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    static long? Long(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : null;
    static string? Upper(string? s) => s?.ToUpperInvariant();
}
