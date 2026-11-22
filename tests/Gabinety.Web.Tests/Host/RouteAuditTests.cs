using System.Net;
using Gabinety.RouteAudit;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;

namespace Gabinety.Web.Tests.Pipeline;

/// <summary>
/// Deny by default (SPEC 11, docs/operations/ROUTE-AUTH.md, as the services do it): every route of the web host is in the table with its policy, a route nobody protected is 401, and the table
/// is the same list the host maps. The component routes carry their policy as page attributes ([AllowAnonymous] or [Authorize]).
/// </summary>
public class RouteAuditTests
{
    const string Service = "Web";

    static bool IsStaticAsset(Endpoint e) => e.Metadata.Any(m => m.GetType().Name.Contains("StaticAsset", StringComparison.Ordinal));

    /// <summary>A Razor component endpoint answers GET and, for its forms, POST: one row, <c>PAGE</c>.</summary>
    static IEnumerable<string> Methods(RouteEndpoint e) =>
        e.Metadata.Any(m => m.GetType().Name == "ComponentTypeMetadata") ? ["PAGE"] : e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["ANY"];

    static string Normalize(string? raw) => "/" + System.Text.RegularExpressions.Regex.Replace(raw ?? "", @"\{(\*?\w+)(:[^}]*)?\}", "{$1}").Trim('/');

    static (IReadOnlyList<RouteRow> Rows, IReadOnlyList<RouteEndpoint> Assets) Actual(IServiceProvider sp)
    {
        var all = sp.GetServices<EndpointDataSource>().SelectMany(s => s.Endpoints).OfType<RouteEndpoint>().ToList();
        var rows = all.Where(e => !IsStaticAsset(e))
            .SelectMany(e => Methods(e).Select(m => new RouteRow(Service, m, Normalize(e.RoutePattern.RawText), RouteTable.PolicyOf(e), "all")))
            .Distinct().OrderBy(r => r.Pattern, StringComparer.Ordinal).ThenBy(r => r.Method, StringComparer.Ordinal).ToList();
        return (rows, all.Where(IsStaticAsset).ToList());
    }

    [Theory]
    [InlineData("Development", true)]  // the profile of a developer's machine and of the fake profile (the table has no `fake` row for the web host today)
    [InlineData("Production", false)]  // the deployed profile: only the `all` rows
    public async Task The_routes_of_the_host_match_the_table_and_none_is_unprotected(string environment, bool development)
    {
        await using var web = new WebFactory().In(environment);
        web.CreateClient();
        var (rows, _) = Actual(web.Services);
        var problems = RouteTable.Diff(RouteTable.Expected(Service), rows, fake: development);
        Assert.True(problems.Count == 0, "\n" + string.Join("\n", problems));
    }

    [Fact]
    public async Task Every_static_asset_is_anonymous_so_the_stylesheet_and_the_framework_script_load_for_a_visitor()
    {
        await using var web = new WebFactory();
        web.CreateClient();
        var (_, assets) = Actual(web.Services);
        Assert.NotEmpty(assets);
        Assert.All(assets, e => Assert.Equal("Anonymous", RouteTable.PolicyOf(e)));
        Assert.Contains(assets, e => e.RoutePattern.RawText!.Contains("_framework/blazor.web"));
        Assert.Contains(assets, e => e.RoutePattern.RawText!.Contains("styles.css"));
    }

    [Fact]
    public async Task The_routes_that_need_a_session_are_exactly_these()
    {
        await using var web = new WebFactory();
        web.CreateClient();
        var (rows, _) = Actual(web.Services);
        Assert.Equal(["/admin", "/admin/ksiega", "/admin/ksiega/csv", "/admin/opinie", "/admin/podejrzane", "/admin/spory", "/admin/weryfikacja", "/admin/weryfikacja/{Id}", "/bookings/{BookingId}", "/checkout/mock/{BookingId}", "/gabinet-programisty", "/gabinet-programisty/dostepnosc", "/gabinet-programisty/oferty", "/gabinet-programisty/platnosci", "/gabinet-programisty/profil", "/konto", "/konto/export", "/moje/opinie", "/moje/opinie/{BookingId}", "/moje/rezerwacje", "/moje/rezerwacje/{BookingId}", "/practitioner/payments", "/rezerwacja/potwierdzenie/{BookingId}", "/rezerwacja/{OfferingId}"],
            rows.Where(r => r.Policy != "Anonymous").Select(r => r.Pattern).Order(StringComparer.Ordinal));
        Assert.All(rows.Where(r => r.Pattern.StartsWith("/admin")), r => Assert.Equal("Admin", r.Policy)); // the whole area is behind the one policy: a page of /admin cannot be added without it
        Assert.DoesNotContain(rows, r => r.Pattern.StartsWith("/api") || r.Pattern.StartsWith("/internal")); // the host serves no API of its own: the browser talks to no backend
    }

    [Fact]
    public async Task The_host_denies_by_default_a_route_with_no_policy_needs_a_signed_in_user()
    {
        // The host registers the kernel's fallback policy (RequireAuthenticatedByDefault). A route nobody protected is evaluated against it: refused for a visitor, allowed for a signed-in user.
        await using var web = new WebFactory();
        web.CreateClient();
        var fallback = await web.Services.GetRequiredService<IAuthorizationPolicyProvider>().GetFallbackPolicyAsync();
        Assert.NotNull(fallback);
        var authorization = web.Services.GetRequiredService<IAuthorizationService>();
        var ctx = new DefaultHttpContext();
        ctx.SetEndpoint(new RouteEndpoint(_ => Task.CompletedTask, RoutePatternFactory.Parse("/test/forgot-the-policy"), 0, new EndpointMetadataCollection(), "probe"));

        var visitor = new ClaimsPrincipal(new ClaimsIdentity());
        var member = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "u")], "Cookies"));
        Assert.False((await authorization.AuthorizeAsync(visitor, ctx, fallback)).Succeeded);
        Assert.True((await authorization.AuthorizeAsync(member, ctx, fallback)).Succeeded);
    }

    [Fact]
    public async Task The_fallback_does_not_turn_an_unmatched_path_into_a_challenge_and_the_hub_and_probes_stay_reachable()
    {
        await using var web = new WebFactory();
        var client = web.CreateClient(new() { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/no/such/page")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/alive")).StatusCode);
    }
}
