using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
    /// X-Internal-Key compared in constant time against Internal:Key (ADR-0007) and, during a rotation, against Internal:PreviousKey (RUNBOOK 5.3).
    /// The previous key is optional; a blank value is the same as absent.
    /// </summary>
    public static AuthenticationBuilder AddInternalKeyAuthentication(this AuthenticationBuilder auth) =>
        auth.AddScheme<AuthenticationSchemeOptions, InternalKeyHandler>(InternalScheme, null);

    /// <summary>
    /// Deny by default (SPEC 11, T-10.12): a route with no <c>RequireAuthorization</c> and no <c>AllowAnonymous</c> needs a signed-in user instead of being public.
    /// A public route must say <c>AllowAnonymous()</c>; docs/operations/ROUTE-AUTH.md and the per-service route test list every route.
    /// </summary>
    public static AuthorizationBuilder RequireAuthenticatedByDefault(this AuthorizationBuilder auth)
    {
        auth.Services.AddSingleton<IAuthorizationHandler, AuthenticatedByDefaultHandler>();
        return auth.SetFallbackPolicy(new AuthorizationPolicyBuilder().AddRequirements(new AuthenticatedByDefaultRequirement()).Build());
    }

    sealed class AuthenticatedByDefaultRequirement : IAuthorizationRequirement;

    /// <summary>
    /// "RequireAuthenticatedUser", but only for a request that matched a route. ASP.NET applies a fallback policy to every request, including one that matched nothing (404) or
    /// matched a path with another method (405); a plain RequireAuthenticatedUser would turn those into 401 and hide that the route is not mapped (CONTRACTS 10.4).
    /// </summary>
    sealed class AuthenticatedByDefaultHandler : AuthorizationHandler<AuthenticatedByDefaultRequirement>
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AuthenticatedByDefaultRequirement requirement)
        {
            var endpoint = context.Resource switch { HttpContext h => h.GetEndpoint(), Endpoint e => e, _ => null };
            if (endpoint is not RouteEndpoint || context.User.Identity?.IsAuthenticated == true) context.Succeed(requirement);
            return Task.CompletedTask;
        }
    }

    public const int DefaultPerMinute = RateLimitTable.DefaultPerMinute;

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

    /// <summary>
    /// The rate limits of CONTRACTS 10.2 (see <see cref="RateLimitTable"/>) on the global limiter: per user id when signed in, else per address; refused with 429
    /// <c>rate_limited</c> and a <c>Retry-After</c>. It runs on <see cref="TimeProvider"/> (registered here as the system clock when nothing else is). The middleware must sit
    /// AFTER <c>UseAuthentication</c>, or the user id is not yet known and every caller is keyed by address. <paramref name="perMinute"/> is "everything else"; see the table for
    /// how a larger value scales the others.
    /// </summary>
    public static IServiceCollection AddRateLimiting(this IServiceCollection services, int perMinute = DefaultPerMinute)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(perMinute);
        services.TryAddSingleton(TimeProvider.System);
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = async (context, ct) =>
            {
                var response = context.HttpContext.Response;
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry))
                    response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retry.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                response.ContentType = "application/problem+json";
                await response.WriteAsync("{\"type\":\"urn:gabinety:error:rate_limited\",\"title\":\"Too many requests\",\"status\":429,\"code\":\"rate_limited\"}", ct);
            };
        });
        services.AddOptions<RateLimiterOptions>().Configure<TimeProvider, IConfiguration>((o, time, config) =>
        {
            var proxyKey = config["RateLimit:TrustedProxyKey"]; // shared with the web app only; see RateLimitTable.PartitionKey
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
            {
                var group = RateLimitTable.GroupOf(ctx.Request.Method, ctx.Request.Path);
                if (group == RateLimitTable.Webhooks) return RateLimitPartition.GetNoLimiter("webhooks");
                return RateLimitPartition.Get(RateLimitTable.PartitionKey(group, ctx, proxyKey), _ => new WindowedRateLimiter(time, RateLimitTable.RulesOf(group, perMinute)));
            });
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
            var given = Request.Headers[InternalKey.HeaderName].ToString();
            if (!InternalKey.Configured(config) || string.IsNullOrEmpty(given)) return Task.FromResult(AuthenticateResult.NoResult());
            if (!InternalKey.Matches(config, given)) return Task.FromResult(AuthenticateResult.Fail("bad internal key"));
            var id = new System.Security.Claims.ClaimsIdentity(InternalScheme);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new System.Security.Claims.ClaimsPrincipal(id), InternalScheme)));
        }
    }
}

/// <summary>The key check of the internal scheme, shared by every service. A key value is never logged and never put in a message.</summary>
public static class InternalKey
{
    public const string HeaderName = "X-Internal-Key";
    public const string CurrentSetting = "Internal:Key", PreviousSetting = "Internal:PreviousKey";

    /// <summary>True when a current key is set; without one the scheme authenticates nobody.</summary>
    public static bool Configured(IConfiguration config) => !string.IsNullOrEmpty(config[CurrentSetting]);

    /// <summary>
    /// True when <paramref name="given"/> equals the current key or the previous one. Both comparisons always run (no short circuit on the first),
    /// each in constant time; an empty or absent key on either side never matches, so an empty previous key cannot match an empty header.
    /// </summary>
    public static bool Matches(IConfiguration config, string? given)
    {
        if (string.IsNullOrEmpty(given) || !Configured(config)) return false; // a previous key alone never opens the door
        var g = Encoding.UTF8.GetBytes(given);
        var current = Equal(config[CurrentSetting], g);
        var previous = Equal(config[PreviousSetting], g);
        return current | previous;
    }

    static bool Equal(string? expected, byte[] given) =>
        !string.IsNullOrEmpty(expected) && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), given);
}
