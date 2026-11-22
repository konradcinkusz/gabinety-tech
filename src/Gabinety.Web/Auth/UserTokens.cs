using Microsoft.AspNetCore.Components.Authorization;

namespace Gabinety.Web.Auth;

/// <summary>What a client wrapper needs from the signed-in user: the bearer token of the CURRENT scope (null when nobody is signed in). A seam, so a component test needs no identity service.</summary>
public interface IAccessTokenSource
{
    Task<string?> AccessTokenAsync(CancellationToken ct = default);
}

/// <summary>
/// The refresh token of the session cookie as this scope saw it. In a request it is read from the request's cookie; a circuit has no <c>HttpContext</c> once it runs, so the circuit
/// handler captures it when the circuit opens (<see cref="SessionCircuitHandler"/>). Scoped; never shown to a component.
/// </summary>
public interface ISessionSeed
{
    Task<string?> RefreshTokenAsync();
    Task CaptureAsync();
}

public sealed class SessionSeed(IHttpContextAccessor accessor) : ISessionSeed
{
    string? _captured;
    bool _read;

    public async Task<string?> RefreshTokenAsync()
    {
        if (!_read && accessor.HttpContext is { } http) { _captured = await SessionCookie.RefreshTokenOfAsync(http) ?? _captured; _read = true; }
        return _captured;
    }

    public Task CaptureAsync() => RefreshTokenAsync();
}

/// <summary>
/// Circuit-safe access to the signed-in user's bearer token (BLAZOR-WEB-APP section 7, the three traps; unchanged contract: <see cref="IAccessTokenSource"/>). (1) It never reads <c>HttpContext</c>
/// for the user: the session id comes from the <see cref="AuthenticationStateProvider"/> of the caller's scope, the request in a static page and the circuit in an interactive one. (2) The
/// client wrappers call it and put the token on the request; a message handler does not see it. (3) The access token is MINTED ON DEMAND from the session's refresh token and cached until
/// it is about to expire (<see cref="SessionCache"/>): a restart or a deploy loses nothing the cookie does not hold. The refresh token is single use: one rotation per session at a time
/// (the keyed lock), the newest token kept for the cookie. A refresh the identity service refuses ends the session; a 429, a 5xx or an outage does not (the old token is used while it lasts and
/// the identity service is not asked again for a few seconds).
/// </summary>
public sealed class UserTokens(AuthenticationStateProvider state, ISessionSeed seed, SessionCache cache, IdentityClient identity, ITokenVerifier verifier, TimeProvider time) : IAccessTokenSource
{
    public static readonly TimeSpan RefreshBefore = TimeSpan.FromSeconds(30), RetryAfterOutage = TimeSpan.FromSeconds(5);

    /// <summary>The access token of the current user, or null when nobody is signed in or the session has ended. Never throws for a missing session.</summary>
    public async Task<string?> AccessTokenAsync(CancellationToken ct = default)
    {
        var user = (await state.GetAuthenticationStateAsync()).User;
        if (user.Identity?.IsAuthenticated != true || user.FindFirst(SessionCookie.SessionClaim)?.Value is not { } sid) return null;
        return await ForSessionAsync(sid, await seed.RefreshTokenAsync(), ct);
    }

    /// <summary>The same for a session id and the seed the caller holds (the sign-out handler has the cookie in hand).</summary>
    public async Task<string?> ForSessionAsync(string sid, string? seedRefreshToken, CancellationToken ct)
    {
        var entry = cache.Find(sid) ?? (seedRefreshToken is null ? null : cache.GetOrSeed(sid, seedRefreshToken));
        if (entry is null || entry.Dead) return null;
        if (Fresh(entry)) return entry.AccessToken;

        await entry.Gate.WaitAsync(ct);
        try
        {
            if (entry.Dead) return null;
            if (Fresh(entry)) return entry.AccessToken; // another caller rotated while this one waited: never spend the refresh token twice
            if (time.GetUtcNow() < entry.RetryNotBefore) return Usable(entry);

            var result = await identity.RefreshAsync(entry.RefreshToken, ct);
            switch (result.Outcome)
            {
                case IdentityOutcome.Tokens when result.AccessToken is { } access && result.RefreshToken is { } refresh:
                    entry.RefreshToken = refresh; // the old one is spent whatever happens next: keep the new one before anything can fail
                    var (verdict, token) = await verifier.VerifyAsync(access, ct);
                    if (verdict == VerifyOutcome.Valid && token is not null)
                    {
                        entry.AccessToken = access;
                        entry.AccessExpiresAt = token.ExpiresAt;
                        entry.Identity = new SessionIdentity(token.Subject, token.Email, token.Roles);
                        entry.RetryNotBefore = default;
                        return access;
                    }
                    entry.AccessToken = null; // a token that does not verify is never trusted
                    entry.RetryNotBefore = time.GetUtcNow() + RetryAfterOutage;
                    return null;
                case IdentityOutcome.Unauthorized or IdentityOutcome.Rejected or IdentityOutcome.TwoFactor or IdentityOutcome.VerificationRequired:
                    cache.End(sid, entry.RefreshToken); // the identity service says this session is over: so is ours, and a circuit notices at its next revalidation
                    return null;
                default: // Unavailable: 429, 5xx, a timeout. The session stays; the old token is used while it lasts.
                    entry.RetryNotBefore = time.GetUtcNow() + RetryAfterOutage;
                    return Usable(entry);
            }
        }
        finally
        {
            entry.Gate.Release();
        }
    }

    bool Fresh(SessionCache.Entry e) => e.AccessToken is not null && e.AccessExpiresAt - time.GetUtcNow() > RefreshBefore;

    string? Usable(SessionCache.Entry e) => e.AccessToken is not null && e.AccessExpiresAt > time.GetUtcNow() ? e.AccessToken : null;
}
