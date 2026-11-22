using Microsoft.AspNetCore.OutputCaching;

namespace Gabinety.Web.Hosting;

/// <summary>
/// Short output caching of the public pages (ADR-0010 cutover precondition; SPEC 6.5 had five minutes for every Directory read in the Next.js app). The rules, each pinned by a host test:
/// only an anonymous <c>GET</c> (or <c>HEAD</c>); never when the request carries the session cookie or an <c>Authorization</c> header; keyed by route AND query (the filters, the cursor, the
/// slot picker's state); never a response that is not a plain 200 (a 404, a 503 or a redirect is never stored) and never one that sets a cookie. <c>OutputCache:Enabled=false</c> turns it
/// off (the E2E host and tests that change a backend between two requests); <c>OutputCache:Seconds</c> overrides the five minutes.
/// </summary>
public sealed class PublicPageCachePolicy(IConfiguration config) : IOutputCachePolicy
{
    public const string Name = "public-pages";
    public const int DefaultSeconds = 300;

    internal TimeSpan Duration => TimeSpan.FromSeconds(Math.Clamp(config.GetValue("OutputCache:Seconds", DefaultSeconds), 1, 3600));
    bool Enabled => config.GetValue("OutputCache:Enabled", true);

    /// <summary>The request is cacheable only when no identity can be involved.</summary>
    public static bool IsAnonymousRead(HttpRequest request) =>
        (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
        && !request.Cookies.ContainsKey(AuthenticationExtensions.CookieName)
        && !request.Headers.ContainsKey("Authorization")
        && request.HttpContext.User.Identity?.IsAuthenticated != true;

    public ValueTask CacheRequestAsync(OutputCacheContext context, CancellationToken cancellation)
    {
        var allowed = Enabled && IsAnonymousRead(context.HttpContext.Request);
        context.EnableOutputCaching = allowed;
        context.AllowCacheLookup = allowed;
        context.AllowCacheStorage = allowed;
        context.AllowLocking = true;
        context.ResponseExpirationTimeSpan = Duration;
        context.CacheVaryByRules.QueryKeys = "*"; // route + every query key: /programisci?duration=30 and /programisci are two entries
        context.CacheVaryByRules.HeaderNames = "Accept-Encoding";
        return ValueTask.CompletedTask;
    }

    public ValueTask ServeFromCacheAsync(OutputCacheContext context, CancellationToken cancellation) => ValueTask.CompletedTask;

    public ValueTask ServeResponseAsync(OutputCacheContext context, CancellationToken cancellation)
    {
        var response = context.HttpContext.Response;
        // Only a plain 200 is stored: an error state, a 404 or a redirect must be asked again, and a response that sets a cookie is personal by definition.
        if (response.StatusCode != StatusCodes.Status200OK || response.Headers.ContainsKey("Set-Cookie")) context.AllowCacheStorage = false;
        return ValueTask.CompletedTask;
    }
}

public static class PublicPageCacheExtensions
{
    public static IServiceCollection AddPublicPageCache(this IServiceCollection services)
    {
        services.AddSingleton<PublicPageCachePolicy>();
        services.AddOutputCache(o => o.AddPolicy(PublicPageCachePolicy.Name, b => b.AddPolicy<PublicPageCachePolicy>(), excludeDefaultPolicy: true));
        return services;
    }
}
