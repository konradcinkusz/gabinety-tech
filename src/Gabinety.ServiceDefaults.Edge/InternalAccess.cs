using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Gabinety.ServiceDefaults;
// Own project (SPEC 6 caps the kernel at 400 lines): src/Gabinety.ServiceDefaults.Edge, same namespace so Program.cs needs no new using.

/// <summary>
/// F-03 (T-10.10): defence in depth for the internal routes. The primary control is the network (only Booking has a public Fly listener, deploy/estate.conf);
/// this keeps <c>/internal/*</c> unreachable even if a listener is made public by mistake, and checks the strength and the separation of the internal keys at start.
/// </summary>
public static class InternalAccess
{
    /// <summary>
    /// Headers the Fly edge proxy sets on a request that arrived from the Internet. A request that reaches a machine directly over the private network
    /// (<c>&lt;app&gt;.internal</c>, 6PN) does not pass the proxy and carries none of them. Not verified against the Fly documentation from this build environment
    /// (fly.io was unreachable): the list is configurable with <c>Internal:EdgeHeaders</c> (comma separated) and the deployment must confirm it (RUNBOOK 11, item 9).
    /// </summary>
    public static readonly string[] DefaultEdgeHeaders =
        ["Fly-Client-IP", "X-Forwarded-For", "Fly-Forwarded-Proto", "Fly-Forwarded-Port", "Fly-Forwarded-Ssl", "X-Forwarded-Proto"];

    /// <summary><c>Internal:RejectPublicEdge</c>; absent means true, except in the fake profile (<c>GABINETY_FAKE=1</c>), where every caller is localhost and there is no edge.</summary>
    public static bool RejectPublicEdge(IConfiguration config) =>
        bool.TryParse(config["Internal:RejectPublicEdge"], out var set) ? set : config["GABINETY_FAKE"] != "1";

    public static bool IsPublicEdge(HttpRequest request, IConfiguration config)
    {
        var raw = config["Internal:EdgeHeaders"];
        var names = string.IsNullOrWhiteSpace(raw)
            ? DefaultEdgeHeaders
            : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var n in names)
            if (request.Headers.ContainsKey(n)) return true;
        return false;
    }

    /// <summary>
    /// Answers 404 to <c>/internal/*</c> (the fake-profile test and demo routes included) when the request came through the public edge, whatever key it carries.
    /// With <paramref name="publicPath"/> (Booking) a public-edge request is served only when the predicate holds; every other path is 404 from the edge.
    /// Requests over the private network are never touched. Put it first in the pipeline.
    /// </summary>
    public static IApplicationBuilder UsePublicEdgeGuard(this IApplicationBuilder app, Func<HttpRequest, bool>? publicPath = null)
    {
        var config = app.ApplicationServices.GetRequiredService<IConfiguration>();
        return app.Use(async (ctx, next) =>
        {
            if (!RejectPublicEdge(config)) { await next(); return; }
            // X-Edge-Key (the BFF's rate-limit proxy key, RateLimitTable) is not an edge header: the web app sends it over the private network to routes Booking filters for the public.
            // But it must never open /internal/*, which stays behind the internal-key policy only, so a request carrying it is refused there like an edge request.
            var internalPath = ctx.Request.Path.StartsWithSegments("/internal", StringComparison.OrdinalIgnoreCase);
            var viaEdge = IsPublicEdge(ctx.Request, config);
            if ((internalPath && (viaEdge || ctx.Request.Headers.ContainsKey(RateLimitTable.TrustedProxyKeyHeader)))
                || (viaEdge && publicPath is not null && !publicPath(ctx.Request)))
            {
                ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            await next();
        });
    }

    // ---- keys ----

    public const int MinKeyLength = 43; // 32 bytes as base64url; 64 hex characters also pass
    const int MinDistinctCharacters = 12;

    public static bool IsStrong(string key)
    {
        if (key.Length < MinKeyLength) return false;
        foreach (var ch in key)
            if (!(char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '+' or '/' or '=')) return false;
        return key.Distinct().Count() >= MinDistinctCharacters;
    }

    /// <summary>
    /// Every configured internal key and the rate-limit proxy key are strong and no two secrets are the same: not two callers on this callee, not the callee key and a caller key,
    /// not two outgoing keys of this caller (<c>Internal:Keys:&lt;Callee&gt;</c>). Messages name the settings, never a value. Throws <see cref="InvalidOperationException"/>.
    /// </summary>
    public static void ValidateKeys(IConfiguration config)
    {
        var named = new List<(string Name, string Key)>();
        foreach (var (caller, key) in InternalKey.Callers(config)) named.Add(($"{InternalKey.CallersSection}:{caller}", key));
        foreach (var n in new[] { InternalKey.CurrentSetting, InternalKey.PreviousSetting })
            if (!string.IsNullOrEmpty(config[n])) named.Add((n, config[n]!));
        foreach (var c in config.GetSection("Internal:Keys").GetChildren())
            if (!string.IsNullOrEmpty(c.Value)) named.Add(($"Internal:Keys:{c.Key}", c.Value));

        if (!string.IsNullOrEmpty(config["RateLimit:TrustedProxyKey"])) named.Add(("RateLimit:TrustedProxyKey", config["RateLimit:TrustedProxyKey"]!));
        var weak = named.Where(n => !IsStrong(n.Key)).Select(n => n.Name).ToList();
        if (weak.Count > 0)
            throw new InvalidOperationException(
                $"Internal key too weak or malformed: {string.Join(", ", weak)}. Use at least {MinKeyLength} characters of base64url or hex (openssl rand -base64 33).");

        foreach (var group in named.GroupBy(n => n.Key, StringComparer.Ordinal).Where(g => g.Count() > 1))
            throw new InvalidOperationException($"Internal keys must be distinct, one per caller and callee: the same value is set for {string.Join(" and ", group.Select(g => g.Name))}.");
    }

    /// <summary>
    /// F-11: token validation must not be fail-open on a missing setting. <c>Identity:Audience</c> is set (without it any token of the same authservice, minted for another audience, is accepted)
    /// and <c>Identity:Authority</c> is an absolute https URL (the JWKS is fetched from it, and the JwtBearer metadata check is off). Messages name the setting, never the value.
    /// </summary>
    public static void ValidateIdentity(IConfiguration config)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(config["Identity:Audience"])) problems.Add("Identity:Audience is not set");
        if (!Uri.TryCreate(config["Identity:Authority"], UriKind.Absolute, out var authority) || authority.Scheme != Uri.UriSchemeHttps) problems.Add("Identity:Authority is not an absolute https URL");
        if (problems.Count > 0) throw new InvalidOperationException($"Token validation is not configured safely: {string.Join("; ", problems)}.");
    }

    /// <summary>
    /// Strength is enforced outside the fake profile and outside the Development environment (an unset environment is Production), or when <c>Internal:EnforceKeyStrength=true</c>;
    /// <c>false</c> turns it off.
    /// </summary>
    public static bool EnforceKeyStrength(IConfiguration config, IHostEnvironment env) =>
        bool.TryParse(config["Internal:EnforceKeyStrength"], out var set) ? set : config["GABINETY_FAKE"] != "1" && !env.IsDevelopment();

    /// <summary>Registers the startup validation of the internal keys, of <c>RateLimit:TrustedProxyKey</c> and (F-11) of the identity settings, all under the same switch (see <see cref="EnforceKeyStrength"/>).</summary>
    public static IServiceCollection AddInternalKeyValidation(this IServiceCollection services) => services.AddHostedService<KeyValidationService>();

    internal sealed class KeyValidationService(IConfiguration config, IHostEnvironment env) : IHostedLifecycleService
    {
        public Task StartingAsync(CancellationToken ct)
        {
            if (EnforceKeyStrength(config, env)) { ValidateKeys(config); ValidateIdentity(config); }
            return Task.CompletedTask;
        }
        public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
        public Task StartedAsync(CancellationToken ct) => Task.CompletedTask;
        public Task StoppingAsync(CancellationToken ct) => Task.CompletedTask;
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
        public Task StoppedAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
