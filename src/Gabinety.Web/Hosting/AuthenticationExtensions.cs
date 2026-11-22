using Gabinety.ServiceDefaults;
using Gabinety.Web.Auth;
using Gabinety.Web.Text;
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
            // F-10: a signed-in user who may not see a page gets the status 403 (the status-code page says "Brak dostępu"), not a silent redirect to "/".
            o.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                // A GET is answered by the status-code page (HTML, "Brak dostępu"). A post must not be: the page would be re-executed as a POST and fail its antiforgery check with a 400, so it gets a plain 403 body instead.
                if (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)) return Task.CompletedTask;
                context.Response.ContentType = "text/plain; charset=utf-8";
                return context.Response.WriteAsync(PlAdmin.Forbidden.Title);
            };
        });
        services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme).Configure<ServerSessionStore, WebOptions, TimeProvider>((o, store, web, time) =>
        {
            o.SessionStore = store;
            o.Cookie.SecurePolicy = web.SecureCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
            o.TimeProvider = time;
        });
        services.AddAuthorizationBuilder().RequireAuthenticatedByDefault().AddAdminPolicy();
        services.AddCascadingAuthenticationState();

        // Antiforgery tokens and the cookie are only valid across restarts and instances when the keys are shared (BLAZOR-WEB-APP section 14): DataProtection:KeysPath, a shared volume.
        var protection = services.AddDataProtection().SetApplicationName("gabinety-web");
        if (builder.Configuration["DataProtection:KeysPath"] is { Length: > 0 } path) protection.PersistKeysToFileSystem(new DirectoryInfo(path));
        return builder;
    }
}
