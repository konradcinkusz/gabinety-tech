using System.Net;
using Gabinety.Booking.Payments.Abstractions;
using Gabinety.RouteAudit;
using Gabinety.ServiceDefaults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gabinety.Booking.Tests;

/// <summary>T-10.12 / F-06: every route is in docs/operations/ROUTE-AUTH.md with its policy, and a route nobody protected is 401.</summary>
public class RouteAuthTests
{
    const string Service = "Booking";

    /// <summary>Mock payments refuse to start outside the fake profile (F-01) and no vendor adapter exists yet (T-09.05): a deployed-profile host gets a double named "Acme".</summary>
    static RouteAuditHost<Program> Host(bool fake, IServiceFeature? extra = null) =>
        new(fake, extra, fake ? null : s => { s.RemoveAll<IPaymentProvider>(); s.AddSingleton<IPaymentProvider>(new RecordingProvider("Acme")); });

    static IReadOnlyList<string> Problems(bool fake)
    {
        using var host = Host(fake);
        host.CreateClient(); // starts the host so every endpoint is built
        return RouteTable.Diff(RouteTable.Expected(Service), RouteTable.Actual(Service, host.Services, fake ? "fake" : "all"), fake);
    }

    [Fact] public void Deployed_profile_routes_match_the_table() => Check(Problems(false));

    [Fact] public void Fake_profile_routes_match_the_table() => Check(Problems(true));

    static void Check(IReadOnlyList<string> p) => Assert.True(p.Count == 0, "\n" + string.Join("\n", p));

    [Fact]
    public async Task A_route_registered_without_a_policy_is_401_not_public()
    {
        using var host = Host(false, new UnprotectedProbe());
        var r = await host.CreateClient().GetAsync("/test/forgot-the-policy");
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task The_fallback_does_not_turn_an_unknown_path_or_a_wrong_method_into_401()
    {
        using var host = Host(false);
        var c = host.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/no/such/route")).StatusCode); // not mapped stays 404 (CONTRACTS 10.4: the fake-profile routes are 404 when deployed)
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await c.PostAsync("/health/details", null)).StatusCode);
    }

    // F-07: the OpenAPI document lists every route, internal and admin ones included, so it is served only in the fake profile (CONTRACTS 0: development and CI).
    [Fact]
    public async Task The_OpenAPI_document_is_served_in_the_fake_profile_and_is_404_when_deployed()
    {
        using var fake = Host(true);
        Assert.Equal(HttpStatusCode.OK, (await fake.CreateClient().GetAsync("/openapi/v1.json")).StatusCode);
        using var deployed = Host(false);
        Assert.Equal(HttpStatusCode.NotFound, (await deployed.CreateClient().GetAsync("/openapi/v1.json")).StatusCode);
    }
}
