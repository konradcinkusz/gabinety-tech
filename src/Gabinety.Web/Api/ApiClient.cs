using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Gabinety.Web.Api;

/// <summary>
/// Shared plumbing of the typed clients: one JSON reading (CONTRACTS 0: camelCase, enums as strings), failures as <see cref="ApiResult{T}"/>, and the request options the
/// <see cref="BackendHeadersHandler"/> reads. A client is a wrapper that runs in the caller's scope (the circuit's, in an interactive page), which is why the token is read here.
/// </summary>
public abstract class ApiClient(HttpClient http, VisitorAddressAccessor visitor)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };

    protected Task<ApiResult<T>> GetAsync<T>(string path, string? accessToken, CancellationToken ct) => SendAsync<T>(HttpMethod.Get, path, accessToken, ct);

    /// <summary>A write (never retried by the resilience handler: SPEC 5 rule 8). <paramref name="body"/> is serialized with the services' options; <paramref name="timeout"/> lifts the attempt budget for the calls SPEC 5.1 gives more time.</summary>
    protected Task<ApiResult<T>> PostAsync<T>(string path, object? body, string? accessToken, CancellationToken ct, IReadOnlyDictionary<string, string>? headers = null, TimeSpan? timeout = null) =>
        SendAsync<T>(HttpMethod.Post, path, accessToken, ct, body, headers, timeout);

    /// <summary>
    /// A response that is passed on as a stream (a CSV export): the headers are read, the body is NOT buffered. The caller owns the result and disposes it (which ends the backend call).
    /// A failure is an <see cref="ApiFailure"/> like any other call's.
    /// </summary>
    protected async Task<(ApiStream? Stream, ApiFailure? Failure)> OpenStreamAsync(string path, string accept, string? accessToken, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        if (!string.IsNullOrEmpty(accessToken)) request.Options.Set(RequestContext.AccessToken, accessToken);
        if (visitor.Current is { } address) request.Options.Set(RequestContext.VisitorAddress, address);
        HttpResponseMessage? response = null;
        try
        {
            response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                var (code, _) = await FailureOf(response, ct);
                var failure = new ApiFailure((int)response.StatusCode, code);
                response.Dispose();
                return (null, failure);
            }
            return (new ApiStream(response, await response.Content.ReadAsStreamAsync(ct)), null);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            response?.Dispose();
            return (null, ApiFailure.NoAnswer);
        }
        finally
        {
            request.Dispose();
        }
    }

    async Task<ApiResult<T>> SendAsync<T>(HttpMethod method, string path, string? accessToken, CancellationToken ct, object? body = null, IReadOnlyDictionary<string, string>? headers = null, TimeSpan? timeout = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null) request.Content = JsonContent.Create(body, body.GetType(), options: Json);
        foreach (var (name, value) in headers ?? new Dictionary<string, string>()) request.Headers.TryAddWithoutValidation(name, value);
        if (timeout is { } budget) request.Options.Set(RequestContext.Timeout, budget);
        if (!string.IsNullOrEmpty(accessToken)) request.Options.Set(RequestContext.AccessToken, accessToken);
        if (visitor.Current is { } address) request.Options.Set(RequestContext.VisitorAddress, address);
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);
            if (!response.IsSuccessStatusCode) { var (code, fields) = await FailureOf(response, ct); return ApiResult<T>.Fail((int)response.StatusCode, code, fields); }
            if (typeof(T) == typeof(Unit)) return ApiResult<T>.Ok((T)(object)Unit.Value);
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
    /// The <c>code</c> of an RFC 9457 body (CONTRACTS 0), else <c>http_&lt;status&gt;</c>, and the NAMES of the keys of <c>errors</c> (field names only, at most 8, each 1-64 letters, digits, dots or underscores).
    /// A title, a detail or any message of the server is never used: they are English and may echo the input.
    /// </summary>
    static async Task<(string Code, IReadOnlyList<string>? Fields)> FailureOf(HttpResponseMessage response, CancellationToken ct)
    {
        var fallback = $"http_{(int)response.StatusCode}";
        try
        {
            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (fallback, null);
            var code = root.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String
                && c.GetString() is { Length: > 0 and <= 64 } text && text.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '_') ? text : fallback;
            var fields = root.TryGetProperty("errors", out var e) && e.ValueKind == JsonValueKind.Object
                ? e.EnumerateObject().Select(p => p.Name).Where(n => n.Length is > 0 and <= 64 && n.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '.')).Take(8).ToList()
                : null;
            return (code, fields);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return (fallback, null);
        }
    }
}

/// <summary>A backend response body being passed on: disposing it releases the connection, which also ends a transfer the client has abandoned.</summary>
public sealed class ApiStream(HttpResponseMessage response, Stream body) : IDisposable
{
    public Stream Body { get; } = body;
    public string? ContentType => response.Content.Headers.ContentType?.ToString();
    public void Dispose() { Body.Dispose(); response.Dispose(); }
}
