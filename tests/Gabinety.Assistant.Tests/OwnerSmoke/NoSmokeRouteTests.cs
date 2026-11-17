using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Gabinety.Assistant.Tests.OwnerSmoke;

/// <summary>The smoke verb is a command-line verb, not an endpoint: the web host has no route for it (T-02.05).</summary>
public class NoSmokeRouteTests
{
    [Fact]
    public async Task The_web_host_has_no_smoke_route()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseSetting("Llm:Provider", "Fake"));
        var routes = factory.Services.GetServices<EndpointDataSource>().SelectMany(d => d.Endpoints)
            .OfType<RouteEndpoint>().Select(e => e.RoutePattern.RawText ?? "").ToList();
        Assert.NotEmpty(routes);
        Assert.DoesNotContain(routes, r => r.Contains("smoke", StringComparison.OrdinalIgnoreCase));

        var client = factory.CreateClient();
        foreach (var path in new[] { "/smoke", "/api/v1/assistant/smoke", "/internal/v1/assistant/smoke" })
        {
            Assert.Equal(System.Net.HttpStatusCode.NotFound, (await client.GetAsync(path)).StatusCode);
            Assert.Equal(System.Net.HttpStatusCode.NotFound, (await client.PostAsync(path, null)).StatusCode);
        }
    }
}
