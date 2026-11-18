using System.Net;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Gabinety.ServiceDefaults;

// Compiled into each service's test project (Link), so every service audits its own route table the same way (T-10.12, F-06).
namespace Gabinety.RouteAudit;

/// <summary>One row of docs/operations/ROUTE-AUTH.md: what a route is and who may call it.</summary>
public sealed record RouteRow(string Service, string Method, string Pattern, string Policy, string Profile)
{
    public string Key => $"{Service} {Method} {Pattern}";
    public override string ToString() => $"| {Service} | {Method} | `{Pattern}` | {Policy} | {Profile} |";
}

public static class RouteTable
{
    public const string Doc = "docs/operations/ROUTE-AUTH.md";

    static string Root()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "GabinetyTech.sln"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("solution root (GabinetyTech.sln) not found");
    }

    /// <summary>The committed table: lines of the form <c>| Service | METHOD | `pattern` | Policy | Profile | ... |</c>.</summary>
    public static IReadOnlyList<RouteRow> Expected(string service)
    {
        var rows = new List<RouteRow>();
        foreach (var line in File.ReadAllLines(Path.Combine(Root(), Doc)))
        {
            var c = line.Split('|').Select(x => x.Trim()).ToArray();
            if (c.Length < 7 || c[1] != service) continue;
            rows.Add(new RouteRow(c[1], c[2], c[3].Trim('`'), c[4], c[5]));
        }
        return rows;
    }

    /// <summary>The authorization an endpoint declares for itself, never the fallback: "(none)" means no metadata at all.</summary>
    public static string PolicyOf(Endpoint e)
    {
        if (e.Metadata.GetMetadata<IAllowAnonymous>() is not null) return "Anonymous";
        var parts = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var a in e.Metadata.GetOrderedMetadata<IAuthorizeData>())
        {
            if (a.Policy is not null) parts.Add(a.Policy);
            else if (a.Roles is not null) foreach (var r in a.Roles.Split(',')) parts.Add($"Role:{r.Trim()}");
            else parts.Add("Authenticated"); // RequireAuthorization() with no argument = the default policy = a signed-in user
        }
        foreach (var p in e.Metadata.GetOrderedMetadata<AuthorizationPolicy>())
            foreach (var r in p.Requirements.OfType<Microsoft.AspNetCore.Authorization.Infrastructure.RolesAuthorizationRequirement>())
                parts.Add("Role:" + string.Join("|", r.AllowedRoles));
        return parts.Count == 0 ? "(none)" : string.Join("+", parts);
    }

    /// <summary>"/a/{id:guid}/" becomes "/a/{id}": constraints and a trailing slash are not part of the route's identity here.</summary>
    static string Normalize(string? raw)
    {
        var p = System.Text.RegularExpressions.Regex.Replace(raw ?? "", @"\{(\*?\w+)(:[^}]*)?\}", "{$1}");
        return "/" + p.Trim('/');
    }

    public static IReadOnlyList<RouteRow> Actual(string service, IServiceProvider sp, string profile) =>
        sp.GetServices<EndpointDataSource>().SelectMany(s => s.Endpoints).OfType<RouteEndpoint>()
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["ANY"])
                .Select(m => new RouteRow(service, m, Normalize(e.RoutePattern.RawText), PolicyOf(e), profile)))
            .OrderBy(r => r.Pattern, StringComparer.Ordinal).ThenBy(r => r.Method, StringComparer.Ordinal).ToList();

    /// <summary>Empty when the host's routes equal the table (for this profile); otherwise one line per problem, each saying what to do.</summary>
    public static IReadOnlyList<string> Diff(IReadOnlyList<RouteRow> expected, IReadOnlyList<RouteRow> actual, bool fake)
    {
        var problems = new List<string>();
        var want = expected.Where(r => fake || r.Profile == "all").ToDictionary(r => r.Key);
        foreach (var a in actual)
        {
            if (a.Policy == "(none)")
                problems.Add($"UNPROTECTED {a.Key}: no RequireAuthorization(...) or AllowAnonymous(). It is only safe because of the fallback policy. Say what you mean: add RequireAuthorization(\"<policy>\") or, if it is public on purpose, AllowAnonymous(), then add the row to {Doc}.");
            if (!want.TryGetValue(a.Key, out var w))
                problems.Add($"NEW route {a.Key} is not in {Doc}. Add the row (Profile `all` if the route is mapped in the deployed profile, `fake` if only in the fake profile): {a}");
            else if (w.Policy != a.Policy)
                problems.Add($"CHANGED policy of {a.Key}: table says '{w.Policy}', code says '{a.Policy}'. If intended, update the row in {Doc} (and say why in the PR).");
        }
        var have = actual.Select(a => a.Key).ToHashSet();
        foreach (var w in want.Values.Where(w => !have.Contains(w.Key)))
            problems.Add($"STALE row {w.Key}: in {Doc} but not mapped (profile {(fake ? "fake" : "deployed")}). Remove the row if the route is gone.");
        return problems;
    }
}

/// <summary>A host of the service, optionally in the fake profile and optionally with one extra feature (a deliberately unprotected route).</summary>
public sealed class RouteAuditHost<TEntry>(bool fake = false, IServiceFeature? extra = null, Action<IServiceCollection>? configure = null) : WebApplicationFactory<TEntry> where TEntry : class
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("GABINETY_FAKE", fake ? "1" : "0");
        builder.UseSetting("Llm:Provider", "Fake");
        if (extra is not null) builder.ConfigureServices(s => s.AddSingleton(extra));
        if (configure is not null) builder.ConfigureServices(configure); // e.g. Booking: a payment provider double for the deployed profile, where Mock refuses to start (F-01)
    }
}

/// <summary>Test-only route with NO authorization metadata: the fallback policy must answer it with 401.</summary>
public sealed class UnprotectedProbe : IServiceFeature
{
    public void Add(IHostApplicationBuilder builder) { }
    public void Map(IEndpointRouteBuilder e) => e.MapGet("/test/forgot-the-policy", () => "ok");
}
