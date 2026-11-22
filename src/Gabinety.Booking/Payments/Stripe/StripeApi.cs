using System.Net.Http.Headers;
using System.Text.Json;
using Gabinety.Booking.Payments.Abstractions;

namespace Gabinety.Booking.Payments.Stripe;

/// <summary>
/// The Stripe REST API over a plain <see cref="HttpClient"/> (no vendor package: ADR proposal 0011). Requests are form encoded with the documented bracket notation;
/// responses are read with System.Text.Json. Every write carries the <c>Idempotency-Key</c> header. Field names are those of Stripe's OpenAPI spec (stripe/openapi,
/// latest/openapi.spec3.json, version <see cref="ApiVersion"/>). Everything in this namespace is internal: no Stripe type or name leaves it.
/// </summary>
internal sealed class StripeApi(HttpClient http, string secretKey)
{
    /// <summary>The API version the requests are made against: the <c>info.version</c> of the OpenAPI spec the field names were taken from. The webhook endpoint at Stripe must be created with the same version (docs/operations/PAYMENTS-GO-LIVE.md).</summary>
    public const string ApiVersion = "2026-11-18.endive";
    public const string IdempotencyHeader = "Idempotency-Key";

    public async Task<JsonElement> PostAsync(string path, StripeForm form, string idempotencyKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey)) throw new ArgumentException("A write call needs an idempotency key.", nameof(idempotencyKey));
        using var request = Request(HttpMethod.Post, path);
        request.Headers.TryAddWithoutValidation(IdempotencyHeader, idempotencyKey);
        request.Content = form.ToContent();
        return await SendAsync(request, ct);
    }

    public async Task<JsonElement> GetAsync(string path, StripeForm? query, CancellationToken ct)
    {
        using var request = Request(HttpMethod.Get, query is null ? path : $"{path}?{query.ToQueryString()}");
        return await SendAsync(request, ct);
    }

    HttpRequestMessage Request(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secretKey);
        request.Headers.TryAddWithoutValidation("Stripe-Version", ApiVersion);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    async Task<JsonElement> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using var response = await http.SendAsync(request, ct);
        var body = await response.Content.ReadAsByteArrayAsync(ct);
        if (!response.IsSuccessStatusCode) throw new PaymentProviderException((int)response.StatusCode, ErrorCode(body));
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.Clone();
        }
        catch (JsonException) { throw new PaymentProviderException((int)response.StatusCode, "unreadable_response"); }
    }

    /// <summary>The <c>error.code</c> (or else <c>error.type</c>) of a failed call: a stable token, never the message.</summary>
    static string? ErrorCode(byte[] body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("error", out var e) || e.ValueKind != JsonValueKind.Object) return null;
            foreach (var name in new[] { "code", "type" })
                if (e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 and <= 64 } s && s.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')) return s;
        }
        catch (JsonException) { }
        return null;
    }
}

/// <summary>A form body in Stripe's bracket notation (<c>line_items[0][quantity]=1</c>).</summary>
internal sealed class StripeForm
{
    readonly List<KeyValuePair<string, string>> fields = [];

    public StripeForm Add(string name, string value) { fields.Add(new(name, value)); return this; }
    public StripeForm Add(string name, long value) => Add(name, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    public StripeForm Add(string name, bool value) => Add(name, value ? "true" : "false");

    public IReadOnlyList<KeyValuePair<string, string>> Fields => fields;
    public FormUrlEncodedContent ToContent() => new(fields);
    public string ToQueryString() => string.Join('&', fields.Select(f => $"{Uri.EscapeDataString(f.Key)}={Uri.EscapeDataString(f.Value)}"));
}
