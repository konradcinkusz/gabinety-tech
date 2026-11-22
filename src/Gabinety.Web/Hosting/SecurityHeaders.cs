namespace Gabinety.Web.Hosting;

/// <summary>
/// The security response headers of PR #113 (<c>web/src/lib/security/headers.ts</c>), reproduced for the Blazor host with the same intent. What differs, and why:
/// <list type="bullet">
/// <item><c>script-src 'self'</c> and nothing else (no nonce, no <c>'strict-dynamic'</c>, never <c>'unsafe-inline'</c> or <c>'unsafe-eval'</c>): the framework script (<c>_framework/blazor.web.js</c>) is a same-origin file and the
/// pages carry no inline script, so there is nothing for a nonce to vouch for; Next needed a nonce because it writes inline bootstrap scripts. A page that ever needs an inline script adds a nonce then.</item>
/// <item><c>connect-src 'self'</c> plus the host's own <c>ws:</c>/<c>wss:</c> origin: the Blazor circuit is a websocket to this host and nothing else; <c>'self'</c> alone is not honoured for websockets by every browser.</item>
/// <item>There is no development exception: Next adds <c>'unsafe-eval'</c> under <c>next dev</c>; Blazor needs none in any environment.</item>
/// <item>No <c>API_CSP</c> split: the host serves no JSON API of its own (the browser talks to no backend), only pages and the framework's endpoints.</item>
/// </list>
/// Same as Next: <c>X-Content-Type-Options: nosniff</c>, <c>Referrer-Policy: strict-origin-when-cross-origin</c>, the permissions policy, <c>X-Frame-Options: DENY</c> and <c>frame-ancestors 'none'</c>, HSTS only where the site is https.
/// </summary>
public static class SecurityHeaders
{
    static readonly string[] DeniedFeatures =
    [
        "accelerometer", "autoplay", "browsing-topics", "camera", "display-capture", "fullscreen", "geolocation", "gyroscope", "hid", "interest-cohort", "magnetometer",
        "microphone", "midi", "payment", "publickey-credentials-get", "screen-wake-lock", "serial", "usb", "xr-spatial-tracking",
    ];

    public static string ContentSecurityPolicy(HttpRequest request)
    {
        var socket = (request.IsHttps ? "wss://" : "ws://") + request.Host;
        return string.Join("; ",
            "default-src 'self'",
            "script-src 'self'",
            "style-src 'self'",
            "img-src 'self' data:",
            "font-src 'self'",
            $"connect-src 'self' {socket}",
            "object-src 'none'",
            "base-uri 'self'",
            "form-action 'self'",
            "frame-ancestors 'none'");
    }

    public static readonly string PermissionsPolicy = string.Join(", ", DeniedFeatures.Select(f => $"{f}=()"));

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app, WebOptions options) => app.Use((ctx, next) =>
    {
        ctx.Response.OnStarting(() =>
        {
            var h = ctx.Response.Headers;
            h.ContentSecurityPolicy = ContentSecurityPolicy(ctx.Request);
            h.XContentTypeOptions = "nosniff";
            h["Referrer-Policy"] = "strict-origin-when-cross-origin";
            h["Permissions-Policy"] = PermissionsPolicy;
            h.XFrameOptions = "DENY";
            if (options.SecureCookies) h.StrictTransportSecurity = "max-age=63072000; includeSubDomains"; // only where the site is served over TLS: on localhost it would pin the host to https
            return Task.CompletedTask;
        });
        return next();
    });
}
