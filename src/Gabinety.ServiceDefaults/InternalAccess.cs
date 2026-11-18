using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Gabinety.ServiceDefaults;

/// <summary>
/// F-03 (T-10.10): defence in depth for the internal routes. The primary control is the network (only Booking has a public Fly listener, deploy/estate.conf);
/// this keeps <c>/internal/*</c> unreachable even if a listener is made public by mistake, and checks the strength and the separation of the internal keys at start.
/// </summary>
public static class InternalAccess
{
    /// <summary>
    /// Headers the Fly edge proxy sets on a request that arrived from the Internet. A request that reaches a machine directly over the private network
    /// (<c>&lt;app&gt;.internal</c>, 6PN) does not pass the proxy and carries none of them. Not verified against the Fly documentation from this build environment
    /// (fly.io was unreachable): the list is configurable with <c>Internal:PublicEdgeHeaders</c> (comma separated) and the deployment must confirm it (RUNBOOK 11, item 9).
    /// </summary>
    public static readonly string[] DefaultPublicEdgeHeaders =
        ["Fly-Client-IP", "X-Forwarded-For", "Fly-Forwarded-Proto", "Fly-Forwarded-Port", "Fly-Forwarded-Ssl", "X-Forwarded-Proto"];

    /// <summary><c>Internal:RejectPublicEdge</c>; absent means true, except in the fake profile (<c>GABINETY_FAKE=1</c>), where every caller is localhost and there is no edge.</summary>
    public static bool RejectPublicEdge(IConfiguration config) =>
        bool.TryParse(config["Internal:RejectPublicEdge"], out var set) ? set : config["GABINETY_FAKE"] != "1";

    public static bool IsPublicEdge(HttpRequest request, IConfiguration config)
    {
        var raw = config["Internal:PublicEdgeHeaders"];
        var names = string.IsNullOrWhiteSpace(raw)
            ? DefaultPublicEdgeHeaders
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
            if (RejectPublicEdge(config) && IsPublicEdge(ctx.Request, config)
                && (ctx.Request.Path.StartsWithSegments("/internal", StringComparison.OrdinalIgnoreCase)
                    || (publicPath is not null && !publicPath(ctx.Request))))
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

    /// <summary>The accepted keys of this callee, by caller. <c>Internal:Callers:&lt;Caller&gt;</c> are the per-caller keys; <c>Internal:Key</c> is the single key of the fake profile and tests, named "shared".</summary>
    internal static IReadOnlyList<(string Caller, string Key)> AcceptedKeys(IConfiguration config)
    {
        var list = new List<(string, string)>();
        foreach (var c in config.GetSection("Internal:Callers").GetChildren())
            if (!string.IsNullOrEmpty(c.Value)) list.Add((c.Key, c.Value));
        if (!string.IsNullOrEmpty(config["Internal:Key"])) list.Add(("shared", config["Internal:Key"]!));
        return list;
    }

    public static bool IsStrong(string key)
    {
        if (key.Length < MinKeyLength) return false;
        foreach (var ch in key)
            if (!(char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '+' or '/' or '=')) return false;
        return key.Distinct().Count() >= MinDistinctCharacters;
    }

    /// <summary>
    /// Every configured internal key is strong and no two secrets are the same: not two callers on this callee, not the callee key and a caller key,
    /// not two outgoing keys of this caller (<c>Internal:Keys:&lt;Callee&gt;</c>). Messages name the settings, never a value. Throws <see cref="InvalidOperationException"/>.
    /// </summary>
    public static void ValidateKeys(IConfiguration config)
    {
        var named = new List<(string Name, string Key)>();
        foreach (var (caller, key) in AcceptedKeys(config)) named.Add((caller == "shared" ? "Internal:Key" : $"Internal:Callers:{caller}", key));
        foreach (var c in config.GetSection("Internal:Keys").GetChildren())
            if (!string.IsNullOrEmpty(c.Value)) named.Add(($"Internal:Keys:{c.Key}", c.Value));

        var weak = named.Where(n => !IsStrong(n.Key)).Select(n => n.Name).ToList();
        if (weak.Count > 0)
            throw new InvalidOperationException(
                $"Internal key too weak or malformed: {string.Join(", ", weak)}. Use at least {MinKeyLength} characters of base64url or hex (openssl rand -base64 33).");

        foreach (var group in named.GroupBy(n => n.Key, StringComparer.Ordinal).Where(g => g.Count() > 1))
            throw new InvalidOperationException($"Internal keys must be distinct, one per caller and callee: the same value is set for {string.Join(" and ", group.Select(g => g.Name))}.");
    }

    /// <summary>
    /// Strength is enforced outside the fake profile and outside the Development environment (an unset environment is Production), or when <c>Internal:EnforceKeyStrength=true</c>;
    /// <c>false</c> turns it off.
    /// </summary>
    public static bool EnforceKeyStrength(IConfiguration config, IHostEnvironment env) =>
        bool.TryParse(config["Internal:EnforceKeyStrength"], out var set) ? set : config["GABINETY_FAKE"] != "1" && !env.IsDevelopment();

    internal sealed class KeyValidationService(IConfiguration config, IHostEnvironment env) : IHostedLifecycleService
    {
        public Task StartingAsync(CancellationToken ct)
        {
            if (EnforceKeyStrength(config, env)) ValidateKeys(config);
            return Task.CompletedTask;
        }
        public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
        public Task StartedAsync(CancellationToken ct) => Task.CompletedTask;
        public Task StoppingAsync(CancellationToken ct) => Task.CompletedTask;
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
        public Task StoppedAsync(CancellationToken ct) => Task.CompletedTask;
    }

    /// <summary>The matching caller (or null), comparing against every accepted key in constant time.</summary>
    internal static string? Match(IConfiguration config, string given)
    {
        var g = Encoding.UTF8.GetBytes(given);
        string? hit = null;
        foreach (var (caller, key) in AcceptedKeys(config))
            if (CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(key), g)) hit ??= caller;
        return hit;
    }
}
