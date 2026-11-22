using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Gabinety.Web.Auth;

public enum SignInOutcome { SignedIn, InvalidCredentials, VerificationRequired, TwoFactor, Unavailable }

/// <summary>
/// Signs a user in through the host (BLAZOR-WEB-APP section 7): the identity service's answer is VERIFIED (signature, issuer, audience, expiry: <see cref="ITokenVerifier"/>), its tokens go to
/// the server-side store, and the cookie that is issued holds only the session id. The principal carries the id, the e-mail, the roles of the token and the session id, nothing else.
/// Never logs a credential, a token or an address.
/// </summary>
public sealed class SignInService(IdentityClient identity, ITokenVerifier verifier, ServerSessionStore store, TimeProvider time)
{
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(7); // the refresh cookie of the Next.js app (REFRESH_MAX_AGE)
    public const string AdminRole = "Admin", SuperAdminRole = "SuperAdmin";

    public async Task<SignInOutcome> SignInAsync(HttpContext http, string email, string password, CancellationToken ct = default)
    {
        var result = await identity.LoginAsync(email, password, ct);
        switch (result.Outcome)
        {
            case IdentityOutcome.Unauthorized or IdentityOutcome.Rejected: return SignInOutcome.InvalidCredentials;
            case IdentityOutcome.VerificationRequired: return SignInOutcome.VerificationRequired;
            case IdentityOutcome.TwoFactor: return SignInOutcome.TwoFactor;
            case not IdentityOutcome.Tokens: return SignInOutcome.Unavailable;
        }
        var (verdict, token) = await verifier.VerifyAsync(result.AccessToken!, ct);
        if (verdict != VerifyOutcome.Valid || token is null) return SignInOutcome.Unavailable; // a token that does not verify is never trusted, and the user is not told why

        var sid = store.Open(new SessionTokens(result.AccessToken!, result.RefreshToken!, token.ExpiresAt));
        var claims = new List<Claim> { new("sub", token.Subject), new(ClaimTypes.NameIdentifier, token.Subject), new(ServerSessionStore.SessionClaim, sid) };
        if (token.Email is { } mail) { claims.Add(new Claim("email", mail)); claims.Add(new Claim(ClaimTypes.Name, mail)); }
        claims.AddRange(token.Roles.Select(r => new Claim(ClaimTypes.Role, r)));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme, ClaimTypes.Name, ClaimTypes.Role));
        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
            new AuthenticationProperties { IsPersistent = true, ExpiresUtc = time.GetUtcNow() + SessionLifetime, AllowRefresh = false });
        return SignInOutcome.SignedIn;
    }

    /// <summary>Ends the session on the identity service (best effort) and here, then deletes the cookie with the attributes it was set with.</summary>
    public async Task SignOutAsync(HttpContext http, CancellationToken ct = default)
    {
        if (http.User.FindFirst(ServerSessionStore.SessionClaim)?.Value is { } sid)
        {
            if (store.TokensOf(sid) is { } tokens) await identity.LogoutAsync(tokens.AccessToken, ct);
            store.Close(sid);
        }
        await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }
}
