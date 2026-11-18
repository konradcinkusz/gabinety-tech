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
    /// X-Internal-Key compared in constant time against Internal:Key (ADR-0007) and, during a rotation, against Internal:PreviousKey (RUNBOOK 5.3).
    /// The previous key is optional; a blank value is the same as absent.
    /// </summary>
    public static AuthenticationBuilder AddInternalKeyAuthentication(this AuthenticationBuilder auth) =>
        auth.AddScheme<AuthenticationSchemeOptions, InternalKeyHandler>(InternalScheme, null);

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
