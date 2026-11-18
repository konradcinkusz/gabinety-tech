using System.Net;
using Gabinety.RouteAudit;

namespace Gabinety.Directory.Tests;

/// <summary>T-10.12 / F-06: every route is in docs/operations/ROUTE-AUTH.md with its policy, and a route nobody protected is 401.</summary>
public class RouteAuthTests
{
    const string Service = "Directory";

    static IReadOnlyList<string> Problems(bool fake)
    {
        using var host = new RouteAuditHost<Program>(fake);
        host.CreateClient(); // starts the host so every endpoint is built
        return RouteTable.Diff(RouteTable.Expected(Service), RouteTable.Actual(Service, host.Services, fake ? "fake" : "all"), fake);
    }

    [Fact] public void Deployed_profile_routes_match_the_table() => Check(Problems(false));

    [Fact] public void Fake_profile_routes_match_the_table() => Check(Problems(true));

    static void Check(IReadOnlyList<string> p) => Assert.True(p.Count == 0, "\n" + string.Join("\n", p));

    [Fact]
    public async Task A_route_registered_without_a_policy_is_401_not_public()
    {
        using var host = new RouteAuditHost<Program>(false, new UnprotectedProbe());
        var r = await host.CreateClient().GetAsync("/test/forgot-the-policy");
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }
}
