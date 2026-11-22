using Gabinety.ServiceDefaults;
using Gabinety.Web.Auth;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Gabinety.Web.Hosting;

public static class AuthenticationExtensions
{
    public const string CookieName = "gt_session";

    /// <summary>
    /// Cookie authentication on the host (BLAZOR-WEB-APP section 7): <c>HttpOnly</c>, <c>SameSite=Lax</c>, host-only, <c>Secure</c> when the site is served over https: the attributes of the cookies
    /// of the Next.js app (CONTRACTS 7). It holds a session id only; the tokens are server side (<see cref="ServerSessionStore"/>). Deny by default (<c>RequireAuthenticatedByDefault</c> of the
    /// kernel, as in the services): a route is public only when it says <c>[AllowAnonymous]</c>, and the route audit test lists every route with its policy.
    /// </summary>
    public static WebApplicationBuilder AddWebAuthentication(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        services.AddSingleton<ServerSessionStore>();
        services.AddSingleton<ITokenVerifier, JwksTokenVerifier>();
        services.AddScoped<SignInService>();
        services.AddScoped<UserTokens>();
        services.AddScoped<IAccessTokenSource>(sp => sp.GetRequiredService<UserTokens>());
        services.Configure<CircuitRevalidationOptions>(builder.Configuration.GetSection("Circuit:Revalidation"));
        services.AddScoped<AuthenticationStateProvider, SessionRevalidatingAuthenticationStateProvider>();

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
        {
            o.Cookie.Name = CookieName;
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Lax;
            o.Cookie.Path = "/";
            o.ExpireTimeSpan = SignInService.SessionLifetime;
            o.SlidingExpiration = false;
            o.LoginPath = ReturnTarget.LoginPath;
            o.ReturnUrlParameter = ReturnTarget.Parameter; // /logowanie?redirect=<path and query>, the parameter the Next.js app uses
            o.AccessDeniedPath = "/";
        });
        services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme).Configure<ServerSessionStore, WebOptions, TimeProvider>((o, store, web, time) =>
        {
            o.SessionStore = store;
            o.Cookie.SecurePolicy = web.SecureCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
            o.TimeProvider = time;
        });
        services.AddAuthorizationBuilder().RequireAuthenticatedByDefault();
        services.AddCascadingAuthenticationState();

        // Antiforgery tokens and the cookie are only valid across restarts and instances when the keys are shared (BLAZOR-WEB-APP section 14): DataProtection:KeysPath, a shared volume.
        var protection = services.AddDataProtection().SetApplicationName("gabinety-web");
        if (builder.Configuration["DataProtection:KeysPath"] is { Length: > 0 } path) protection.PersistKeysToFileSystem(new DirectoryInfo(path));
        return builder;
    }
}
