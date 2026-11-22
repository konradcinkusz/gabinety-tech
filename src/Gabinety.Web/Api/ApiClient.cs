using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Gabinety.Web.Api;

/// <summary>
/// Shared plumbing of the typed clients: one JSON reading (CONTRACTS 0: camelCase, enums as strings), failures as <see cref="ApiResult{T}"/>, and the request options the
/// <see cref="BackendHeadersHandler"/> reads. A client is a wrapper that runs in the caller's scope (the circuit's, in an interactive page), which is why the token is read here.
/// </summary>
public abstract class ApiClient(HttpClient http, VisitorAddressAccessor visitor)
{
    /// <summary>An absent optional field is omitted, not sent as null (CONTRACTS 0): writing skips nulls; reading is unaffected.</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };

    protected Task<ApiResult<T>> GetAsync<T>(string path, string? accessToken, CancellationToken ct) => SendAsync<T>(HttpMethod.Get, path, accessToken, ct);

    /// <summary>A write: the body (when there is one) is sent as JSON. Writes are never retried by the resilience handler (SPEC 5 rule 8). Use <see cref="NoContent"/> as <typeparamref name="T"/> for a <c>204</c>.</summary>
    protected Task<ApiResult<T>> WriteAsync<T>(HttpMethod method, string path, string? accessToken, object? body, CancellationToken ct) => SendAsync<T>(method, path, accessToken, ct, body);

    async Task<ApiResult<T>> SendAsync<T>(HttpMethod method, string path, string? accessToken, CancellationToken ct, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null) request.Content = JsonContent.Create(body, body.GetType(), options: Json);
        if (!string.IsNullOrEmpty(accessToken)) request.Options.Set(RequestContext.AccessToken, accessToken);
        if (visitor.Current is { } address) request.Options.Set(RequestContext.VisitorAddress, address);
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);
            if (!response.IsSuccessStatusCode) return ApiResult<T>.Fail(await FailureOf(response, ct));
            if (typeof(T) == typeof(NoContent)) return ApiResult<T>.Ok((T)(object)NoContent.Value);
            var value = await response.Content.ReadFromJsonAsync<T>(Json, ct);
            return value is null ? ApiResult<T>.Fail((int)response.StatusCode, ApiFailure.InvalidResponse) : ApiResult<T>.Ok(value);
        }
        catch (JsonException)
        {
            return ApiResult<T>.Fail(502, ApiFailure.InvalidResponse);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // Down, timed out, circuit open, or no address configured for this backend: the page shows an error state of that region, never an exception. Nothing of the cause is kept.
            return ApiResult<T>.Fail(ApiFailure.NoAnswer);
        }
    }

    /// <summary>
    /// The status, the <c>code</c> of an RFC 9457 body (CONTRACTS 0, else <c>http_&lt;status&gt;</c>), and the NAMES of the fields it complains about (<c>errors</c> keys) and of the unmet checklist items
    /// (<c>unmetItems</c>), each reduced to a short identifier. A title, a detail, a message or any other text of the server is never used.
    /// </summary>
    static async Task<ApiFailure> FailureOf(HttpResponseMessage response, CancellationToken ct)
    {
        var status = (int)response.StatusCode;
        var plain = new ApiFailure(status, $"http_{status}");
        try
        {
            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return plain;
            var code = root.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String && c.GetString() is { Length: > 0 and <= 64 } id && id.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '_') ? id : plain.Code;
            var fields = root.TryGetProperty("errors", out var e) && e.ValueKind == JsonValueKind.Object ? e.EnumerateObject().Select(p => Identifier(p.Name)).OfType<string>().Take(32).ToArray() : [];
            var unmet = root.TryGetProperty("unmetItems", out var u) && u.ValueKind == JsonValueKind.Array ? u.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => Identifier(x.GetString())).OfType<string>().Take(32).ToArray() : [];
            return new ApiFailure(status, code) { Fields = fields, Unmet = unmet };
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return plain;
        }
    }

    /// <summary>An identifier the server may name a code or a field with: letters, digits, <c>_</c>, <c>.</c>, <c>[</c>, <c>]</c> and <c>-</c>, at most 64 characters. Anything else is not trusted.</summary>
    static string? Identifier(string? raw) => raw is { Length: > 0 and <= 64 } && raw.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '.' or '[' or ']' or '-') ? raw : null;
}
