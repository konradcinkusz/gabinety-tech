using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
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
    /// X-Internal-Key compared in constant time against Internal:Callers:&lt;Caller&gt; (one key per caller, the <c>caller</c> claim), Internal:Key (fake profile, tests) and,
    /// during a rotation, Internal:PreviousKey (RUNBOOK 5.3). Blank is absent. Strength and distinctness are checked at start by Gabinety.ServiceDefaults.Edge (F-03).
    /// </summary>
    public static AuthenticationBuilder AddInternalKeyAuthentication(this AuthenticationBuilder auth) =>
        auth.AddScheme<AuthenticationSchemeOptions, InternalKeyHandler>(InternalScheme, null);

    /// <summary>Deny by default (SPEC 11, T-10.12; routes: docs/operations/ROUTE-AUTH.md): a matched route with no policy needs a signed-in user. A request that matched no route stays 404/405 (the fallback also sees those).</summary>
    public static AuthorizationBuilder RequireAuthenticatedByDefault(this AuthorizationBuilder auth) =>
        auth.SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAssertion(c =>
            c.Resource is not HttpContext h || h.GetEndpoint() is not Microsoft.AspNetCore.Routing.RouteEndpoint || c.User.Identity?.IsAuthenticated == true).Build());

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
            var caller = InternalKey.Caller(config, given);
            if (caller is null) return Task.FromResult(AuthenticateResult.Fail("bad internal key"));
            var id = new System.Security.Claims.ClaimsIdentity(InternalScheme);
            id.AddClaim(new System.Security.Claims.Claim("caller", caller));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new System.Security.Claims.ClaimsPrincipal(id), InternalScheme)));
        }
    }
}

/// <summary>The key check of the internal scheme, shared by every service. A key value is never logged and never put in a message.</summary>
public static class InternalKey
{
    public const string HeaderName = "X-Internal-Key", CurrentSetting = "Internal:Key", PreviousSetting = "Internal:PreviousKey";

    public const string CallersSection = "Internal:Callers", SharedCaller = "shared";
    /// <summary>True when a key is set (Internal:Key or any Internal:Callers:*); without one the scheme authenticates nobody.</summary>
    public static bool Configured(IConfiguration config) => !string.IsNullOrEmpty(config[CurrentSetting]) || Callers(config).Any();

    /// <summary>The per-caller keys (F-03), blank ones skipped.</summary>
    public static IEnumerable<(string Caller, string Key)> Callers(IConfiguration config) =>
        config.GetSection(CallersSection).GetChildren().Where(c => !string.IsNullOrEmpty(c.Value)).Select(c => (c.Key, c.Value!));

    /// <summary>
    /// The caller whose key equals <paramref name="given"/> ("shared" for Internal:Key and PreviousKey), else null. Every comparison runs, each in constant time; an empty key never matches.
    /// A previous key alone never opens the door: it counts only while a current key is set.
    /// </summary>
    public static string? Caller(IConfiguration config, string? given)
    {
        if (string.IsNullOrEmpty(given) || !Configured(config)) return null;
        var g = Encoding.UTF8.GetBytes(given);
        var hit = Callers(config).Where(c => Equal(c.Key, g)).Select(c => c.Caller).ToList().FirstOrDefault(); // ToList: every comparison runs
        var shared = Equal(config[CurrentSetting], g) | (!string.IsNullOrEmpty(config[CurrentSetting]) && Equal(config[PreviousSetting], g));
        return shared ? hit ?? SharedCaller : hit;
    }

    public static bool Matches(IConfiguration config, string? given) => Caller(config, given) is not null;
    static bool Equal(string? expected, byte[] given) =>
        !string.IsNullOrEmpty(expected) && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), given);
}
