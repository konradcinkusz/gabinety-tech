using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Gabinety.ServiceDefaults;
// Own project (SPEC 6 caps the kernel at 400 lines): src/Gabinety.ServiceDefaults.Edge, same namespace so Program.cs needs no new using.

/// <summary>
/// BLAZOR-WEB-APP section 8: requests from another site that CORS does not cover. A browser sends a cross-site <c>POST</c> (a simple form post) without a preflight, and a
/// websocket handshake is not subject to CORS at all, so a foreign page can open a Blazor circuit to this host. One middleware answers both, for every method and path:
/// <list type="bullet">
/// <item>a request whose <c>Origin</c> is not in the allowed set is refused (so <c>POST /_blazor/negotiate</c> and the <c>GET /_blazor</c> upgrade of a foreign page are);</item>
/// <item>a request with <c>Sec-Fetch-Site: cross-site</c> is refused, except a top-level <c>GET</c>/<c>HEAD</c> navigation (<c>Sec-Fetch-Mode: navigate</c>,
/// <c>Sec-Fetch-Dest: document</c>), which changes nothing and whose response the other site cannot read: a link followed from another site still loads the page;</item>
/// <item>a request with no <c>Origin</c> and no <c>Sec-Fetch-Site</c> (curl, the server-side HttpClient, health probes, Playwright's request context) is allowed.</item>
/// </list>
/// It is a mitigation, not authentication: it does not stop code already on the machine or a non-browser client that sends neither header. CORS stays unconfigured:
/// the guard refuses foreign origins, it never grants any. Answers 403 <c>cross_site_request</c> (CONTRACTS 10.1), the same status and code as the services and the Next.js BFF.
/// </summary>
public static class BrowserOriginGuard
{
    /// <summary>Extra origins besides the request's own and <c>Site:BaseUrl</c>: <c>AllowedOrigins</c>, a comma separated list or an indexed section (<c>AllowedOrigins__0</c>).</summary>
    public const string AllowedOriginsSetting = "AllowedOrigins", SiteBaseUrlSetting = "Site:BaseUrl";

    public const string Code = "cross_site_request";

    /// <summary>The pure decision, so every rule is tested with plain strings. <paramref name="allowed"/> holds origins as <c>scheme://host[:port]</c>.</summary>
    public static bool IsRefused(string method, string? origin, string? fetchSite, string? fetchMode, string? fetchDest, ISet<string> allowed)
    {
        if (!string.IsNullOrEmpty(origin) && !allowed.Contains(origin.TrimEnd('/'))) return true;
        if (!string.Equals(fetchSite, "cross-site", StringComparison.OrdinalIgnoreCase)) return false;
        var navigation = (HttpMethods.IsGet(method) || HttpMethods.IsHead(method))
            && string.Equals(fetchMode, "navigate", StringComparison.OrdinalIgnoreCase)
            && string.Equals(fetchDest, "document", StringComparison.OrdinalIgnoreCase);
        return !navigation;
    }

    /// <summary>The configured origins: <c>Site:BaseUrl</c> (the public address, which a TLS-terminating proxy hides from the request) and <c>AllowedOrigins</c>. Never a path.</summary>
    public static IReadOnlySet<string> ConfiguredOrigins(IConfiguration config)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? raw)
        {
            if (Uri.TryCreate(raw?.Trim(), UriKind.Absolute, out var u) && (u.Scheme is "http" or "https")) set.Add(u.GetLeftPart(UriPartial.Authority));
        }
        Add(config[SiteBaseUrlSetting]);
        var section = config.GetSection(AllowedOriginsSetting);
        foreach (var part in (section.Value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) Add(part);
        foreach (var child in section.GetChildren()) Add(child.Value);
        return set;
    }

    /// <summary>
    /// Place it FIRST in the pipeline, after forwarded headers (behind a proxy the request's own host is not the browser's) and before antiforgery, authentication and the component
    /// endpoints. The allowed set is the request's own <c>scheme://host</c> plus <see cref="ConfiguredOrigins"/>.
    /// </summary>
    public static IApplicationBuilder UseBrowserOriginGuard(this IApplicationBuilder app)
    {
        var configured = ConfiguredOrigins(app.ApplicationServices.GetRequiredService<IConfiguration>());
        return app.Use(async (ctx, next) =>
        {
            var r = ctx.Request;
            var origin = r.Headers.Origin.ToString();
            var allowed = new HashSet<string>(configured, StringComparer.OrdinalIgnoreCase) { $"{r.Scheme}://{r.Host}" };
            if (IsRefused(r.Method, origin, r.Headers["Sec-Fetch-Site"], r.Headers["Sec-Fetch-Mode"], r.Headers["Sec-Fetch-Dest"], allowed))
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                ctx.Response.ContentType = "application/problem+json";
                await ctx.Response.WriteAsync($"{{\"type\":\"urn:gabinety:error:{Code}\",\"title\":\"Forbidden\",\"status\":403,\"code\":\"{Code}\"}}");
                return;
            }
            await next();
        });
    }
}
