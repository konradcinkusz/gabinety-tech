using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace Gabinety.Web.Auth;

/// <summary>What a client wrapper needs from the signed-in user: the bearer token of the CURRENT scope (null when nobody is signed in). A seam, so a component test needs no identity service.</summary>
public interface IAccessTokenSource
{
    Task<string?> AccessTokenAsync(CancellationToken ct = default);
}

/// <summary>
/// Circuit-safe access to the signed-in user's bearer token (BLAZOR-WEB-APP section 7, the three traps). (1) It never reads <c>HttpContext</c>: the session id comes from the
/// <see cref="AuthenticationStateProvider"/> of the caller's scope, which is the request in a static page and the circuit in an interactive one. (2) It is a scoped service used by the
/// client wrappers, which put the token on the request; a message handler does not see it, because handlers live in their own scope. (3) The token comes from
/// <see cref="ServerSessionStore"/>, which ends with the cookie: a circuit that outlives the sign-in finds no session and gets no token. An expiring access token is refreshed here, once at
/// a time per session (the refresh token is single use); a refresh the identity service refuses closes the session.
/// </summary>
public sealed class UserTokens(AuthenticationStateProvider state, ServerSessionStore store, IdentityClient identity, ITokenVerifier verifier, TimeProvider time) : IAccessTokenSource
{
    static readonly TimeSpan RefreshBefore = TimeSpan.FromSeconds(30);

    /// <summary>The access token of the current user, or null when nobody is signed in or the session has ended. Never throws for a missing session.</summary>
    public async Task<string?> AccessTokenAsync(CancellationToken ct = default)
    {
        var user = (await state.GetAuthenticationStateAsync()).User;
        if (user.Identity?.IsAuthenticated != true || user.FindFirst(ServerSessionStore.SessionClaim)?.Value is not { } sid) return null;
        var tokens = store.TokensOf(sid);
        if (tokens is null) return null;
        if (tokens.AccessExpiresAt - time.GetUtcNow() > RefreshBefore) return tokens.AccessToken;
        return await RefreshAsync(sid, ct);
    }

    async Task<string?> RefreshAsync(string sid, CancellationToken ct)
    {
        var gate = store.RefreshGate(sid);
        if (gate is null) return null;
        await gate.WaitAsync(ct);
        try
        {
            var current = store.TokensOf(sid);
            if (current is null) return null;
            if (current.AccessExpiresAt - time.GetUtcNow() > RefreshBefore) return current.AccessToken; // another caller refreshed while this one waited

            var result = await identity.RefreshAsync(current.RefreshToken, ct);
            if (result.Outcome == IdentityOutcome.Tokens && result.AccessToken is { } access && result.RefreshToken is { } refresh)
            {
                var (verdict, token) = await verifier.VerifyAsync(access, ct);
                if (verdict == VerifyOutcome.Valid && token is not null)
                {
                    store.Update(sid, new SessionTokens(access, refresh, token.ExpiresAt));
                    return access;
                }
            }
            if (result.Outcome is IdentityOutcome.Unauthorized or IdentityOutcome.Rejected or IdentityOutcome.TwoFactor or IdentityOutcome.VerificationRequired)
            {
                store.Close(sid); // the identity service says this session is over: so is ours, and a circuit will notice at its next revalidation
                return null;
            }
            // Unavailable: keep the session and use the token while it still lasts.
            return current.AccessExpiresAt > time.GetUtcNow() ? current.AccessToken : null;
        }
        finally
        {
            gate.Release();
        }
    }
}
