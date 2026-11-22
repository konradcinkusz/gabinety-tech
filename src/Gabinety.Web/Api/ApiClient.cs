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

    /// <summary>A state-changing call (never retried by the resilience handler: POST). A success needs no body: <see cref="Done"/>. <paramref name="body"/> null sends none.</summary>
    protected Task<ApiResult<Done>> PostAsync(string path, string? accessToken, object? body, CancellationToken ct) => SendAsync<Done>(HttpMethod.Post, path, accessToken, ct, body);

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
                var failure = new ApiFailure((int)response.StatusCode, await CodeOf(response, ct));
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
            if (!response.IsSuccessStatusCode) return ApiResult<T>.Fail((int)response.StatusCode, await CodeOf(response, ct));
            if (typeof(T) == typeof(Done)) return ApiResult<T>.Ok((T)(object)Done.Value);
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

    /// <summary>The <c>code</c> of an RFC 9457 body (CONTRACTS 0), else <c>http_&lt;status&gt;</c>. A title, a detail or any other text of the server is never used.</summary>
    static async Task<string> CodeOf(HttpResponseMessage response, CancellationToken ct)
    {
        var fallback = $"http_{(int)response.StatusCode}";
        try
        {
            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String
                && c.GetString() is { Length: > 0 and <= 64 } code && code.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '_') ? code : fallback;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return fallback;
        }
    }
}

/// <summary>The success of a call whose answer has no content (204) or whose content the page does not read.</summary>
public sealed record Done
{
    public static Done Value { get; } = new();
}

/// <summary>A backend response body being passed on: disposing it releases the connection, which also ends a transfer the client has abandoned.</summary>
public sealed class ApiStream(HttpResponseMessage response, Stream body) : IDisposable
{
    public Stream Body { get; } = body;
    public string? ContentType => response.Content.Headers.ContentType?.ToString();
    public void Dispose() { Body.Dispose(); response.Dispose(); }
}
