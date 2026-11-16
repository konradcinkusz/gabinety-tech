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

    /// <summary>X-Internal-Key compared in constant time against Internal:Key (ADR-0007).</summary>
    public static AuthenticationBuilder AddInternalKeyAuthentication(this AuthenticationBuilder auth) =>
        auth.AddScheme<AuthenticationSchemeOptions, InternalKeyHandler>(InternalScheme, null);

    /// <summary>Sliding window per user id when signed in, else per address (CONTRACTS 10.2). Default: 60 per minute.</summary>
    public static IServiceCollection AddRateLimiting(this IServiceCollection services, int perMinute = 60)
    {
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
            var expected = config["Internal:Key"];
            var given = Request.Headers["X-Internal-Key"].ToString();
            if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(given)) return Task.FromResult(AuthenticateResult.NoResult());
            var ok = CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(given));
            if (!ok) return Task.FromResult(AuthenticateResult.Fail("bad internal key"));
            var id = new System.Security.Claims.ClaimsIdentity(InternalScheme);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new System.Security.Claims.ClaimsPrincipal(id), InternalScheme)));
        }
    }
}
