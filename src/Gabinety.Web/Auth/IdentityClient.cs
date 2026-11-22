using System.Text;
using System.Text.Json;
using Gabinety.Web.Api;

namespace Gabinety.Web.Auth;

/// <summary><c>Accepted</c> is the 202 of registration: an e-mail confirmation is pending and no token is issued.</summary>
public enum IdentityOutcome { Tokens, Accepted, TwoFactor, VerificationRequired, Unauthorized, Rejected, Unavailable }

public sealed record IdentityResult(IdentityOutcome Outcome, string? AccessToken = null, string? RefreshToken = null, int ExpiresInSeconds = 3600);

/// <summary>
/// The sign-in calls of the identity service (authservice, or DevIdentity in the fake profile), mirroring <c>web/src/lib/session/identity.ts</c>: the same paths, the same answer
/// classification. Server side only; the tokens it returns go to <see cref="ServerSessionStore"/> and nowhere else. A body or a title of the service is never kept.
/// </summary>
public sealed class IdentityClient(HttpClient http, VisitorAddressAccessor visitor)
{
    public const string ClientIpHeader = "Fly-Client-IP";

    public Task<IdentityResult> LoginAsync(string email, string password, CancellationToken ct = default) =>
        PostAsync("/api/v1/auth/login", new { email, password }, null, ct);

    public Task<IdentityResult> RefreshAsync(string refreshToken, CancellationToken ct = default) =>
        PostAsync("/api/v1/auth/refresh", new { refreshToken }, null, ct);

    /// <summary>The versions of the terms and the privacy text a registration must accept (null when the identity service cannot say).</summary>
    public async Task<(string Terms, string Privacy)?> ConsentVersionsAsync(CancellationToken ct = default)
    {
        using var request = Request("/api/v1/auth/consents/versions", null, null, HttpMethod.Get);
        try
        {
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;
            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var r = doc.RootElement;
            return r.ValueKind == JsonValueKind.Object && r.TryGetProperty("terms", out var t) && t.ValueKind == JsonValueKind.String && r.TryGetProperty("privacy", out var p) && p.ValueKind == JsonValueKind.String
                ? (t.GetString()!, p.GetString()!) : null;
        }
        catch (Exception) when (!ct.IsCancellationRequested) { return null; }
    }

    /// <summary>Registration (the page is ported later): the accepted versions are the current ones unless given. 202 is <see cref="IdentityOutcome.Accepted"/>.</summary>
    public async Task<IdentityResult> RegisterAsync(string email, string password, string? termsVersion = null, string? privacyVersion = null, string? locale = null, CancellationToken ct = default)
    {
        if (termsVersion is null || privacyVersion is null)
        {
            if (await ConsentVersionsAsync(ct) is not { } v) return new IdentityResult(IdentityOutcome.Unavailable);
            (termsVersion, privacyVersion) = (termsVersion ?? v.Terms, privacyVersion ?? v.Privacy);
        }
        return await PostAsync("/api/v1/auth/register", new { email, password, acceptedTermsVersion = termsVersion, acceptedPrivacyVersion = privacyVersion, locale }, null, ct);
    }

    /// <summary>The GitHub leg's one-time code, exchanged for tokens (PR #118).</summary>
    public Task<IdentityResult> ExchangeAsync(string code, CancellationToken ct = default) => PostAsync("/api/v1/external-auth/exchange", new { code }, null, ct);

    /// <summary>Best effort: the local session ends either way.</summary>
    public async Task<bool> LogoutAsync(string accessToken, CancellationToken ct = default)
    {
        using var request = Request("/api/v1/auth/logout", null, accessToken);
        try { using var response = await http.SendAsync(request, ct); return response.IsSuccessStatusCode; }
        catch (Exception) when (!ct.IsCancellationRequested) { return false; }
    }

    async Task<IdentityResult> PostAsync(string path, object body, string? bearer, CancellationToken ct)
    {
        using var request = Request(path, body, bearer);
        try
        {
            using var response = await http.SendAsync(request, ct);
            return await Classify(response, ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return new IdentityResult(IdentityOutcome.Unavailable);
        }
    }

    HttpRequestMessage Request(string path, object? body, string? bearer, HttpMethod? method = null)
    {
        var request = new HttpRequestMessage(method ?? HttpMethod.Post, path);
        if (body is not null) request.Content = new StringContent(JsonSerializer.Serialize(body, ApiClient.Json), Encoding.UTF8, "application/json");
        if (!string.IsNullOrEmpty(bearer)) request.Options.Set(RequestContext.AccessToken, bearer);
        if (visitor.Current is { } address)
        {
            request.Options.Set(RequestContext.VisitorAddress, address);
            // authservice is deployed with Network__ClientIpHeader=Fly-Client-IP (as the Next.js app, PR #124): it takes the visitor's address from this header, so visitors do not share
            // this host's address and its rate limit. The address is the validated one of ClientAddress, never a copy of an incoming header.
            request.Headers.TryAddWithoutValidation(ClientIpHeader, address);
        }
        return request;
    }

    static async Task<IdentityResult> Classify(HttpResponseMessage response, CancellationToken ct)
    {
        var status = (int)response.StatusCode;
        if (status == 200)
        {
            try
            {
                using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return new IdentityResult(IdentityOutcome.Unavailable);
                if (root.TryGetProperty("requiresTwoFactor", out var two) && two.ValueKind == JsonValueKind.True) return new IdentityResult(IdentityOutcome.TwoFactor);
                if (root.TryGetProperty("accessToken", out var at) && at.ValueKind == JsonValueKind.String && root.TryGetProperty("refreshToken", out var rt) && rt.ValueKind == JsonValueKind.String)
                {
                    var expires = root.TryGetProperty("expiresIn", out var e) && e.TryGetInt32(out var n) && n > 0 ? n : 3600;
                    return new IdentityResult(IdentityOutcome.Tokens, at.GetString(), rt.GetString(), expires);
                }
            }
            catch (JsonException) { }
            return new IdentityResult(IdentityOutcome.Unavailable);
        }
        if (status == 429) return new IdentityResult(IdentityOutcome.Unavailable); // rate limited: a refresh that is throttled is not a refused one, the session must not end
        if (status == 202) return new IdentityResult(IdentityOutcome.Accepted);
        if (status == 401) return new IdentityResult(IdentityOutcome.Unauthorized);
        if (status == 403)
        {
            try
            {
                using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("emailVerificationRequired", out var v) && v.ValueKind == JsonValueKind.True)
                    return new IdentityResult(IdentityOutcome.VerificationRequired);
            }
            catch (JsonException) { }
            return new IdentityResult(IdentityOutcome.Rejected);
        }
        return status is >= 400 and < 500 ? new IdentityResult(IdentityOutcome.Rejected) : new IdentityResult(IdentityOutcome.Unavailable);
    }
}
