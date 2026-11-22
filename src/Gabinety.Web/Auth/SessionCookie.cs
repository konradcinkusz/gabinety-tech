using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Gabinety.Web.Auth;

/// <summary>
/// The stateless session (ADR-0010 cutover precondition): the cookie <c>gt_session</c> is the cookie handler's ticket, sealed with Data Protection (the persisted, shared key ring), and holds
/// the claims of the user, a random session id and the identity service's REFRESH token. Never the access token (it is re-minted on demand, <see cref="UserTokens"/>), never a password.
/// The refresh token is kept as an authentication token of the ticket, not as a claim: no component can read it from the principal. The cookie stays far below 4 KB (a size budget test).
/// </summary>
public static class SessionCookie
{
    public const string SessionClaim = "sid", ExpiryClaim = "sexp", RefreshTokenName = "refresh_token";
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7); // the refresh lifetime of authservice and of the Next.js cookie (REFRESH_MAX_AGE)
    const string CatchUpFlag = "gabinety.session.catchup";

    public static string NewSessionId() => Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    public static ClaimsPrincipal Principal(string sid, SessionIdentity who, DateTimeOffset expires)
    {
        var claims = new List<Claim> { new("sub", who.Subject), new(ClaimTypes.NameIdentifier, who.Subject), new(SessionClaim, sid), new(ExpiryClaim, expires.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture)) };
        if (who.Email is { } mail) { claims.Add(new Claim("email", mail)); claims.Add(new Claim(ClaimTypes.Name, mail)); }
        claims.AddRange(who.Roles.Select(r => new Claim(ClaimTypes.Role, r)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme, ClaimTypes.Name, ClaimTypes.Role));
    }

    public static AuthenticationProperties Properties(string refreshToken, DateTimeOffset expires)
    {
        var props = new AuthenticationProperties { IsPersistent = true, ExpiresUtc = expires, AllowRefresh = false };
        props.StoreTokens([new AuthenticationToken { Name = RefreshTokenName, Value = refreshToken }]);
        return props;
    }

    public static Task WriteAsync(HttpContext http, string sid, SessionIdentity who, string refreshToken, TimeProvider time)
    {
        var expires = time.GetUtcNow() + Lifetime; // each rotation renews the lifetime, as the refresh token's own does
        return http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, Principal(sid, who, expires), Properties(refreshToken, expires));
    }

    /// <summary>The refresh token of the cookie of this request (the seed of a session this process has not seen yet), or null for no session.</summary>
    public static async Task<string?> RefreshTokenOfAsync(HttpContext http) =>
        (await http.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme)).Properties?.GetTokenValue(RefreshTokenName);

    /// <summary>
    /// <c>OnValidatePrincipal</c> of the cookie: a session the identity service ended is refused at once (and its cookie deleted), and every other response carries the NEWEST refresh token
    /// into the cookie. A rotation may have happened in a circuit, which has no response to write a cookie into; the next response that can, does (<see cref="CatchUpAsync"/>).
    /// </summary>
    public static Task ValidateAsync(CookieValidatePrincipalContext context, SessionCache cache)
    {
        if (context.Principal?.FindFirst(SessionClaim)?.Value is not { } sid) { context.RejectPrincipal(); return Task.CompletedTask; }
        if (cache.Find(sid) is { Dead: true }) { context.RejectPrincipal(); return context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme); }
        var http = context.HttpContext;
        if (!http.Items.ContainsKey(CatchUpFlag) && !IsFramework(http.Request.Path))
        {
            http.Items[CatchUpFlag] = true;
            var inCookie = context.Properties.GetTokenValue(RefreshTokenName);
            var time = http.RequestServices.GetRequiredService<TimeProvider>();
            http.Response.OnStarting(() => CatchUpAsync(http, cache, sid, inCookie, time));
        }
        return Task.CompletedTask;
    }

    static bool IsFramework(PathString path) => path.StartsWithSegments("/_blazor") || path.StartsWithSegments("/_framework");

    /// <summary>Runs as the response starts (the page is rendered and the tokens are as new as they get): ends the cookie of a session that died meanwhile, or writes the rotated refresh token.</summary>
    public static async Task CatchUpAsync(HttpContext http, SessionCache cache, string sid, string? inCookie, TimeProvider time)
    {
        if (http.Response.HasStarted || cache.Find(sid) is not { } entry) return;
        if (entry.Dead) { await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme); return; }
        if (entry.Identity is null || string.Equals(entry.RefreshToken, inCookie, StringComparison.Ordinal)) return;
        await WriteAsync(http, sid, entry.Identity, entry.RefreshToken, time);
    }
}
