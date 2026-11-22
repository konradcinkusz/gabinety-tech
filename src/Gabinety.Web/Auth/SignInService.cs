using Gabinety.Web.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Gabinety.Web.Auth;

/// <summary><c>ConfirmationPending</c>: the identity service accepted a registration (202) and wants the e-mail confirmed first; nobody is signed in. <c>TwoFactor</c>: a code is needed (the challenge is sealed into a cookie). <c>InvalidCredentials</c> is the generic 401, <c>Rejected</c> a 400 (registration refused), <c>RateLimited</c> a 429: wait.</summary>
public enum SignInOutcome { SignedIn, ConfirmationPending, InvalidCredentials, LockedOut, RateLimited, Rejected, VerificationRequired, TwoFactor, Unavailable }

/// <summary>
/// Signs a user in through the host (BLAZOR-WEB-APP section 7): the identity service's answer is VERIFIED (signature, issuer, audience, expiry: <see cref="ITokenVerifier"/>), the access token goes to
/// the cache of the session and the cookie that is issued holds the refresh token (sealed, see <see cref="SessionCookie"/>). The principal carries the id, the e-mail, the roles of the token and the session id, nothing else.
/// Never logs a credential, a token or an address.
/// </summary>
public sealed class SignInService(IdentityClient identity, ITokenVerifier verifier, SessionCache cache, UserTokens tokens, TwoFactorChallenge twoFactor, WebOptions web, TimeProvider time)
{
        public async Task<SignInOutcome> SignInAsync(HttpContext http, string email, string password, string redirect = "/", CancellationToken ct = default) =>
        await CompleteAsync(http, await identity.LoginAsync(email, password, ct), redirect, ct);

    /// <summary>Registration: the versions are the ones the page showed (sent back verbatim); signs in when the identity service issues tokens at once, else says what is pending.</summary>
    public async Task<SignInOutcome> RegisterAsync(HttpContext http, string email, string password, string? termsVersion, string? privacyVersion, string redirect = "/", CancellationToken ct = default) =>
        await CompleteAsync(http, await identity.RegisterAsync(email, password, termsVersion, privacyVersion, "pl", ct), redirect, ct);

    /// <summary>The GitHub leg: the one-time code becomes a session exactly like a password sign-in.</summary>
    public async Task<SignInOutcome> SignInWithExchangeCodeAsync(HttpContext http, string code, string redirect = "/", CancellationToken ct = default) =>
        await CompleteAsync(http, await identity.ExchangeAsync(code, ct), redirect, ct);

    /// <summary>The second step of a two-factor sign-in: an authenticator code or a recovery code, with the challenge the first step left in its cookie. A wrong code keeps the challenge (a retry); the other failures end it.</summary>
    public async Task<SignInOutcome> CompleteTwoFactorAsync(HttpContext http, string? code, string? recoveryCode, CancellationToken ct = default)
    {
        if (twoFactor.Read(http) is not { } pending) return SignInOutcome.InvalidCredentials;
        var outcome = await CompleteAsync(http, await identity.TwoFactorAsync(pending.ChallengeToken, code, recoveryCode, ct), pending.Redirect, ct);
        if (outcome is not (SignInOutcome.InvalidCredentials or SignInOutcome.RateLimited or SignInOutcome.Unavailable)) twoFactor.Clear(http, web.SecureCookies);
        return outcome;
    }

    async Task<SignInOutcome> CompleteAsync(HttpContext http, IdentityResult result, string redirect, CancellationToken ct)
    {
        switch (result.Outcome)
        {
            case IdentityOutcome.Accepted: return SignInOutcome.ConfirmationPending;
            case IdentityOutcome.Unauthorized: return SignInOutcome.InvalidCredentials;
            case IdentityOutcome.LockedOut: return SignInOutcome.LockedOut;
            case IdentityOutcome.RateLimited: return SignInOutcome.RateLimited;
            case IdentityOutcome.Rejected: return SignInOutcome.Rejected;
            case IdentityOutcome.VerificationRequired: return SignInOutcome.VerificationRequired;
            case IdentityOutcome.TwoFactor:
                if (result.ChallengeToken is not { Length: > 0 } challenge) return SignInOutcome.Unavailable; // a challenge without its token cannot be answered
                twoFactor.Issue(http, challenge, redirect, web.SecureCookies);
                return SignInOutcome.TwoFactor;
            case not IdentityOutcome.Tokens: return SignInOutcome.Unavailable;
        }
        var (verdict, token) = await verifier.VerifyAsync(result.AccessToken!, ct);
        if (verdict != VerifyOutcome.Valid || token is null) return SignInOutcome.Unavailable; // a token that does not verify is never trusted, and the user is not told why

        var who = new SessionIdentity(token.Subject, token.Email, token.Roles);
        var sid = SessionCookie.NewSessionId();
        cache.Open(sid, result.AccessToken!, token.ExpiresAt, result.RefreshToken!, who);
        await SessionCookie.WriteAsync(http, sid, who, result.RefreshToken!, time);
        return SignInOutcome.SignedIn;
    }

    /// <summary>
    /// Ends the session: revokes it on the identity service (best effort; its logout revokes every refresh token of the user), marks it over here so the same cookie is refused at once,
    /// then deletes the cookie with the attributes it was set with. A cookie that is not (or no longer) valid is deleted all the same.
    /// </summary>
    public async Task SignOutAsync(HttpContext http, CancellationToken ct = default)
    {
        if (http.User.FindFirst(SessionCookie.SessionClaim)?.Value is { } sid && await SessionCookie.RefreshTokenOfAsync(http) is { } seed)
        {
            if (await tokens.ForSessionAsync(sid, seed, ct) is { } access) await identity.LogoutAsync(access, ct);
            cache.End(sid, seed);
        }
        await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }
}
