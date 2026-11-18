using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Gabinety.ServiceDefaults;

public static class Security
{
    public const string InternalScheme = "InternalKey";

    /// <summary>Validates RS256 tokens against the JWKS of Identity:Authority. No signing key lives here (P5).</summary>
    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfiguration config)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
        {
            o.Authority = config["Identity:Authority"];
            o.RequireHttpsMetadata = false;
            o.TokenValidationParameters.ValidateAudience = !string.IsNullOrEmpty(config["Identity:Audience"]);
            o.TokenValidationParameters.ValidAudience = config["Identity:Audience"];
        });
        services.AddAuthorization();
        return services;
    }

    /// <summary>
    /// X-Internal-Key compared in constant time against the caller keys <c>Internal:Callers:&lt;Caller&gt;</c> (one per caller, the matching caller becomes the <c>caller</c> claim)
    /// and <c>Internal:Key</c> (the single key of the fake profile). Outside the fake profile and Development the service refuses to start unless every key is strong and distinct (F-03, ADR-0007).
    /// </summary>
    public static AuthenticationBuilder AddInternalKeyAuthentication(this AuthenticationBuilder auth)
    {
        auth.Services.AddHostedService<InternalAccess.KeyValidationService>();
        return auth.AddScheme<AuthenticationSchemeOptions, InternalKeyHandler>(InternalScheme, null);
    }

    public const int DefaultPerMinute = 60;

    /// <summary>
    /// The same limiter with the limit read from <c>RateLimit:PerMinute</c> (env <c>RateLimit__PerMinute</c>); absent or blank means 60. Only the AppHost fake profile raises it,
    /// because every caller there shares one localhost address. A value that is not a positive integer stops the service at start.
    /// </summary>
    public static IServiceCollection AddRateLimiting(this IServiceCollection services, IConfiguration config)
    {
        var raw = config["RateLimit:PerMinute"];
        if (string.IsNullOrWhiteSpace(raw)) return services.AddRateLimiting(DefaultPerMinute);
        if (!int.TryParse(raw, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var perMinute) || perMinute <= 0)
            throw new InvalidOperationException("RateLimit:PerMinute must be a positive integer.");
        return services.AddRateLimiting(perMinute);
    }

    /// <summary>Sliding window per user id when signed in, else per address (CONTRACTS 10.2). Default: 60 per minute.</summary>
    public static IServiceCollection AddRateLimiting(this IServiceCollection services, int perMinute = DefaultPerMinute)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(perMinute);
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    ctx.User.FindFirst("sub")?.Value ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new SlidingWindowRateLimiterOptions
                    { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1), SegmentsPerWindow = 6, QueueLimit = 0 }));
        });
        return services;
    }

    /// <summary>Refuses a state-changing request from a foreign origin with 403 cross_site_request (SPEC 5 rule 12).</summary>
    public static IApplicationBuilder UseOriginGuard(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
    {
        var m = ctx.Request.Method;
        var changes = HttpMethods.IsPost(m) || HttpMethods.IsPut(m) || HttpMethods.IsDelete(m) || HttpMethods.IsPatch(m);
        if (changes)
        {
            var site = ctx.Request.Headers["Sec-Fetch-Site"].ToString();
            var origin = ctx.Request.Headers.Origin.ToString();
            var foreign = site == "cross-site" ||
                (origin.Length > 0 && !string.Equals(origin, $"{ctx.Request.Scheme}://{ctx.Request.Host}", StringComparison.OrdinalIgnoreCase));
            if (foreign)
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                await ctx.Response.WriteAsJsonAsync(new { status = 403, code = "cross_site_request", title = "Forbidden" });
                return;
            }
        }
        await next();
    });

    private sealed class InternalKeyHandler(IOptionsMonitor<AuthenticationSchemeOptions> o, Microsoft.Extensions.Logging.ILoggerFactory l,
        UrlEncoder e, IConfiguration config) : AuthenticationHandler<AuthenticationSchemeOptions>(o, l, e)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var given = Request.Headers["X-Internal-Key"].ToString();
            if (string.IsNullOrEmpty(given)) return Task.FromResult(AuthenticateResult.NoResult());
            var caller = InternalAccess.Match(config, given);
            if (caller is null) return Task.FromResult(AuthenticateResult.Fail("bad internal key"));
            var id = new System.Security.Claims.ClaimsIdentity(InternalScheme);
            id.AddClaim(new System.Security.Claims.Claim("caller", caller));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new System.Security.Claims.ClaimsPrincipal(id), InternalScheme)));
        }
    }
}
