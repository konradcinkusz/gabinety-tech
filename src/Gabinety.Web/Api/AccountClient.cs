using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Gabinety.Web.Auth;

namespace Gabinety.Web.Api;

/// <summary>
/// The account calls of the identity service for the signed-in user (<c>web/src/components/auth/account.server.ts</c>): who I am, my consents, my data export, deleting the account. The bearer
/// is the caller's (<see cref="IAccessTokenSource"/>) unless one is passed (an endpoint that is not a component has no authentication state, so it passes the token it minted). Failures are
/// state (<see cref="ApiResult{T}"/>), never a body or a title of the service.
/// </summary>
public sealed class AccountClient(HttpClient http, VisitorAddressAccessor visitor, IAccessTokenSource tokens)
{
    public const string ConfirmationWord = "DELETE";

    public async Task<ApiResult<AccountDto>> LoadAsync(string? accessToken = null, CancellationToken ct = default)
    {
        var token = accessToken ?? await tokens.AccessTokenAsync(ct);
        if (string.IsNullOrEmpty(token)) return ApiResult<AccountDto>.Fail(401, "unauthenticated");
        var meTask = SendAsync(HttpMethod.Get, "/api/v1/auth/me", null, token, ct);
        var consentsTask = SendAsync(HttpMethod.Get, "/api/v1/auth/consents", null, token, ct);
        await Task.WhenAll(meTask, consentsTask);
        using var me = await meTask;
        using var consents = await consentsTask;
        if (me is null || consents is null) return ApiResult<AccountDto>.Fail(ApiFailure.NoAnswer);
        if (!me.IsSuccessStatusCode || !consents.IsSuccessStatusCode)
            return ApiResult<AccountDto>.Fail(me.StatusCode == HttpStatusCode.Unauthorized || consents.StatusCode == HttpStatusCode.Unauthorized ? 401 : 502, ApiFailure.Unavailable);
        var account = Parse(await Read(me, ct), await Read(consents, ct));
        return account is null ? ApiResult<AccountDto>.Fail(502, ApiFailure.InvalidResponse) : ApiResult<AccountDto>.Ok(account);
    }

    /// <summary>Accepts the current versions of the terms and the privacy text. True when the service says so.</summary>
    public async Task<bool> AcceptCurrentConsentsAsync(string? accessToken = null, CancellationToken ct = default)
    {
        var token = accessToken ?? await tokens.AccessTokenAsync(ct);
        if (string.IsNullOrEmpty(token)) return false;
        using var response = await SendAsync(HttpMethod.Post, "/api/v1/auth/consents", new { acceptedTerms = true, acceptedPrivacy = true }, token, ct);
        return response?.IsSuccessStatusCode == true;
    }

    /// <summary>The user's data as the identity service exports it, passed through as bytes (it is small). 401 stays 401, another refusal is a 502, no answer is a 503.</summary>
    public async Task<ApiResult<byte[]>> ExportAsync(string? accessToken = null, CancellationToken ct = default)
    {
        var token = accessToken ?? await tokens.AccessTokenAsync(ct);
        if (string.IsNullOrEmpty(token)) return ApiResult<byte[]>.Fail(401, "unauthenticated");
        using var response = await SendAsync(HttpMethod.Get, "/api/v1/auth/export", null, token, ct);
        if (response is null) return ApiResult<byte[]>.Fail(503, ApiFailure.Unavailable);
        if (!response.IsSuccessStatusCode) return ApiResult<byte[]>.Fail(response.StatusCode == HttpStatusCode.Unauthorized ? 401 : 502, ApiFailure.Unavailable);
        return ApiResult<byte[]>.Ok(await response.Content.ReadAsByteArrayAsync(ct));
    }

    /// <summary>Soft-deletes the account. The password goes only when given (a GitHub-only account has none); the confirmation word is always sent.</summary>
    public async Task<DeleteOutcome> DeleteAsync(string? password, string? accessToken = null, CancellationToken ct = default)
    {
        var token = accessToken ?? await tokens.AccessTokenAsync(ct);
        if (string.IsNullOrEmpty(token)) return DeleteOutcome.Unauthenticated;
        object body = string.IsNullOrEmpty(password) ? new { confirmation = ConfirmationWord } : new { password, confirmation = ConfirmationWord };
        using var response = await SendAsync(HttpMethod.Delete, "/api/v1/auth/account", body, token, ct);
        if (response is null) return DeleteOutcome.Unavailable;
        if (response.IsSuccessStatusCode) return DeleteOutcome.Deleted;
        if (response.StatusCode == HttpStatusCode.Unauthorized) return DeleteOutcome.Unauthenticated;
        return (int)response.StatusCode >= 500 ? DeleteOutcome.Unavailable : DeleteOutcome.Rejected;
    }

    /// <summary>Builds the account from the two answers field by field, tolerant like the Next.js reader: the version shown is the accepted one, or the required one while pending. Null without an e-mail.</summary>
    public static AccountDto? Parse(JsonElement? me, JsonElement? consents)
    {
        var email = Text(me, "email");
        if (email.Length == 0) return null;
        return new AccountDto(email, Is(me, "emailConfirmed", JsonValueKind.True), !Is(me, "hasPassword", JsonValueKind.False), Part(consents, "terms"), Part(consents, "privacy"));
    }

    static ConsentDto Part(JsonElement? all, string name)
    {
        JsonElement? part = all is { ValueKind: JsonValueKind.Object } o && o.TryGetProperty(name, out var v) ? v : null;
        var accepted = Is(part, "accepted", JsonValueKind.True);
        return new ConsentDto(accepted, Text(part, accepted ? "acceptedVersion" : "requiredVersion"));
    }

    static string Text(JsonElement? o, string name) =>
        o is { ValueKind: JsonValueKind.Object } e && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    static bool Is(JsonElement? o, string name, JsonValueKind kind) =>
        o is { ValueKind: JsonValueKind.Object } e && e.TryGetProperty(name, out var v) && v.ValueKind == kind;

    static async Task<JsonElement?> Read(HttpResponseMessage response, CancellationToken ct)
    {
        try { using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct); return doc.RootElement.Clone(); }
        catch (JsonException) { return null; }
    }

    async Task<HttpResponseMessage?> SendAsync(HttpMethod method, string path, object? body, string token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null) request.Content = new StringContent(JsonSerializer.Serialize(body, ApiClient.Json), Encoding.UTF8, "application/json");
        request.Options.Set(RequestContext.AccessToken, token);
        if (visitor.Current is { } address) request.Options.Set(RequestContext.VisitorAddress, address);
        try { return await http.SendAsync(request, ct); }
        catch (Exception) when (!ct.IsCancellationRequested) { return null; } // down, timed out, circuit open: state, never a cause
    }
}
