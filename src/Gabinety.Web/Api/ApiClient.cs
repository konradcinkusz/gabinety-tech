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

    async Task<ApiResult<T>> SendAsync<T>(HttpMethod method, string path, string? accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrEmpty(accessToken)) request.Options.Set(RequestContext.AccessToken, accessToken);
        if (visitor.Current is { } address) request.Options.Set(RequestContext.VisitorAddress, address);
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);
            if (!response.IsSuccessStatusCode) return ApiResult<T>.Fail((int)response.StatusCode, await CodeOf(response, ct));
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
