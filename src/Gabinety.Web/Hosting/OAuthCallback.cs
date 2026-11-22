using System.Security.Cryptography;
using System.Text;
using Gabinety.Web.Auth;

namespace Gabinety.Web.Hosting;

/// <summary>
/// GitHub sign-in (PR #118, <c>web/src/lib/session/handlers.ts</c>): <c>GET /oauth/github/start</c> and <c>GET /oauth/callback</c>, both ANONYMOUS and not caught by the sign-in redirect (the visitor
/// is not signed in yet, and a bounce through <c>/logowanie</c> would lose <c>code</c> and <c>state</c>). authservice binds only its own GitHub leg to the browser, so a nonce of ours does the rest:
/// a random state goes into the cookie <c>gt_oauth_state</c> (HttpOnly, Lax, 10 minutes) AND into <c>returnUrl</c> (authservice keeps its query and appends <c>&amp;code=</c>); the callback accepts a
/// code only when the two match (constant time) and deletes the cookie on every outcome. <c>returnUrl</c> comes from <c>Site:BaseUrl</c>, never from the request's own host. No token, code or state is logged.
/// </summary>
public static class OAuthEndpoints
{
    public const string StartPath = "/oauth/github/start", StateCookie = "gt_oauth_state";
    static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);

    public static void MapOAuth(this IEndpointRouteBuilder app)
    {
        app.MapGet(StartPath, Start).AllowAnonymous();
        app.MapGet(ReturnTarget.OAuthCallbackPath, Callback).AllowAnonymous();
    }

    static IResult Start(HttpContext http, WebOptions web, string? redirect)
    {
        var authority = web.IdentityPublicUrl ?? (web.Backends[Api.Backend.Auth].Count > 0 ? web.Backends[Api.Backend.Auth][0] : null);
        if (authority is null) return Results.Redirect($"{ReturnTarget.LoginPath}?error=github_failed");
        var state = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var back = ReturnTarget.Safe(redirect);
        var returnUrl = $"{web.SiteBaseUrl}{ReturnTarget.OAuthCallbackPath}?{(back == "/" ? "" : $"redirect={Uri.EscapeDataString(back)}&")}state={state}";
        http.Response.Cookies.Append(StateCookie, state, Options(web, StateLifetime));
        http.Response.Headers.CacheControl = "no-store";
        return Results.Redirect($"{authority}/api/v1/external-auth/login?provider=GitHub&returnUrl={Uri.EscapeDataString(returnUrl)}");
    }

    static async Task<IResult> Callback(HttpContext http, WebOptions web, SignInService signIn, string? code, string? state, string? redirect)
    {
        var fromCookie = http.Request.Cookies[StateCookie];
        if (fromCookie is not null) http.Response.Cookies.Delete(StateCookie, Options(web, TimeSpan.Zero)); // single use: every outcome deletes it, with the attributes it was set with
        http.Response.Headers.CacheControl = "no-store";
        if (!StateMatches(state, fromCookie) || string.IsNullOrEmpty(code)) return Results.Redirect($"{ReturnTarget.LoginPath}?error=github_failed");
        return await signIn.SignInWithExchangeCodeAsync(http, code, ReturnTarget.Safe(redirect), http.RequestAborted) switch
        {
            SignInOutcome.SignedIn => Results.Redirect(ReturnTarget.Safe(redirect)),
            SignInOutcome.TwoFactor => Results.Redirect(ReturnTarget.TwoFactorUrl(ReturnTarget.Safe(redirect))), // the challenge is sealed in its cookie; the code is asked for on its own page
            _ => Results.Redirect($"{ReturnTarget.LoginPath}?error=github_failed"),
        };
    }

    static CookieOptions Options(WebOptions web, TimeSpan maxAge) =>
        new() { HttpOnly = true, SameSite = SameSiteMode.Lax, Path = "/", Secure = web.SecureCookies, MaxAge = maxAge, IsEssential = true };

    /// <summary>Constant-time equality; false for an empty value on either side.</summary>
    public static bool StateMatches(string? a, string? b) =>
        !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
