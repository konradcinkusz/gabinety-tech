using Microsoft.AspNetCore.DataProtection;

namespace Gabinety.Web.Auth;

/// <summary>
/// The pause between the two steps of a two-factor sign-in (a password or GitHub step that answered "a code is needed"): the identity service's challenge token (valid 5 minutes, audience
/// <c>:2fa</c>, useless as a bearer) and where to go afterwards, sealed with Data Protection into the cookie <c>gt_2fa</c> (HttpOnly, SameSite=Lax, 5 minutes, only on the sign-in paths).
/// Never in a URL, a form field or a log. A sealed value that is tampered with, expired or from another purpose is "no challenge".
/// </summary>
public sealed class TwoFactorChallenge(IDataProtectionProvider protection, TimeProvider time)
{
    public const string CookieName = "gt_2fa", Path = "/logowanie";
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    public sealed record Pending(string ChallengeToken, string Redirect);

    ITimeLimitedDataProtector Protector => protection.CreateProtector("gabinety.web.two-factor-challenge.v1").ToTimeLimitedDataProtector();

    public string Seal(string challengeToken, string redirect) =>
        Protector.Protect($"{challengeToken}\n{ReturnTarget.Safe(redirect)}", time.GetUtcNow() + Lifetime);

    public Pending? Unseal(string? sealedValue)
    {
        if (string.IsNullOrEmpty(sealedValue) || sealedValue.Length > 4096) return null;
        try
        {
            var plain = Protector.Unprotect(sealedValue, out var expires);
            if (expires <= time.GetUtcNow()) return null;
            var split = plain.IndexOf('\n');
            return split > 0 ? new Pending(plain[..split], ReturnTarget.Safe(plain[(split + 1)..])) : null;
        }
        catch (System.Security.Cryptography.CryptographicException) { return null; }
    }

    public void Issue(HttpContext http, string challengeToken, string redirect, bool secure) =>
        http.Response.Cookies.Append(CookieName, Seal(challengeToken, redirect), Options(secure, Lifetime));

    public void Clear(HttpContext http, bool secure) => http.Response.Cookies.Delete(CookieName, Options(secure, TimeSpan.Zero));

    public Pending? Read(HttpContext http) => Unseal(http.Request.Cookies[CookieName]);

    static CookieOptions Options(bool secure, TimeSpan maxAge) => new() { HttpOnly = true, SameSite = SameSiteMode.Lax, Path = Path, Secure = secure, MaxAge = maxAge, IsEssential = true };
}
