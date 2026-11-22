using System.Security.Claims;
using Gabinety.Web.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Gabinety.Web.Auth;

public sealed record VerifiedToken(string Subject, string? Email, IReadOnlyList<string> Roles, DateTimeOffset ExpiresAt);

public enum VerifyOutcome { Valid, Invalid, Unavailable }

public interface ITokenVerifier
{
    Task<(VerifyOutcome Outcome, VerifiedToken? Token)> VerifyAsync(string accessToken, CancellationToken ct);
}

/// <summary>
/// Verifies the SIGNATURE (RS256 only) against the JWKS of the identity service, the expiry, and issuer and audience when configured, before a cookie is issued (BLAZOR-WEB-APP section 7,
/// the Blazor form of FRONTEND-BFF section 4: a token that is only decoded accepts a forged payload). The JWKS is cached for five minutes and read again once when a token names an
/// unknown key id (a rotation), at most every 30 seconds. An identity service that cannot be reached is <see cref="VerifyOutcome.Unavailable"/>, not "invalid".
/// </summary>
public sealed class JwksTokenVerifier(IHttpClientFactory clients, WebOptions options, TimeProvider time) : ITokenVerifier
{
    static readonly TimeSpan KeyLifetime = TimeSpan.FromMinutes(5), MinRefetch = TimeSpan.FromSeconds(30), Skew = TimeSpan.FromSeconds(5);

    readonly object _gate = new();
    JsonWebKeySet? _keys;
    DateTimeOffset _fetched = DateTimeOffset.MinValue;

    public async Task<(VerifyOutcome Outcome, VerifiedToken? Token)> VerifyAsync(string accessToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(accessToken)) return (VerifyOutcome.Invalid, null);
        var keys = await KeysAsync(force: false, ct);
        if (keys is null) return (VerifyOutcome.Unavailable, null);
        var result = await Validate(accessToken, keys);
        if (!result.IsValid && result.Exception is SecurityTokenSignatureKeyNotFoundException or SecurityTokenInvalidSignatureException)
        {
            var again = await KeysAsync(force: true, ct); // a rotation: the key id is new
            if (again is not null && !ReferenceEquals(again, keys)) result = await Validate(accessToken, again);
        }
        if (!result.IsValid) return (VerifyOutcome.Invalid, null);

        var claims = result.ClaimsIdentity.Claims.ToList();
        var sub = ClaimMapping.Subject(claims);
        var exp = ((JsonWebToken)result.SecurityToken).ValidTo;
        if (string.IsNullOrEmpty(sub)) return (VerifyOutcome.Invalid, null);
        var roles = ClaimMapping.Roles(claims);
        return (VerifyOutcome.Valid, new VerifiedToken(sub, ClaimMapping.Email(claims), roles, new DateTimeOffset(DateTime.SpecifyKind(exp, DateTimeKind.Utc))));
    }

    Task<TokenValidationResult> Validate(string token, JsonWebKeySet keys) => new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
    {
        IssuerSigningKeys = keys.GetSigningKeys(),
        ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
        RequireSignedTokens = true,
        RequireExpirationTime = true,
        ValidateIssuer = options.IdentityIssuer is not null,
        ValidIssuer = options.IdentityIssuer,
        ValidateAudience = options.IdentityAudience is not null,
        ValidAudience = options.IdentityAudience,
        ValidateLifetime = true,
        LifetimeValidator = (_, expires, _, _) => expires is not null && expires.Value.ToUniversalTime() + Skew > time.GetUtcNow(),
    });

    async Task<JsonWebKeySet?> KeysAsync(bool force, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        lock (_gate)
        {
            if (_keys is not null && now - _fetched < (force ? MinRefetch : KeyLifetime)) return _keys;
        }
        try
        {
            var json = await clients.CreateClient(Api.Backend.Auth).GetStringAsync("/.well-known/jwks.json", ct);
            var set = new JsonWebKeySet(json);
            lock (_gate) { _keys = set; _fetched = now; }
            return set;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            lock (_gate) return _keys; // the last known keys keep verifying while the identity service is away
        }
    }
}
