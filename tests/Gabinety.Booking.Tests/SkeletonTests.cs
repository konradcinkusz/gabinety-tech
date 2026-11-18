using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Gabinety.ServiceDefaults;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Gabinety.Booking.Tests;

/// <summary>Test-only endpoints behind the product's policies; they live in the test host, not in the product.</summary>
sealed class PolicyProbeFeature : IServiceFeature
{
    public void Add(IHostApplicationBuilder builder) { }
    public void Map(IEndpointRouteBuilder e)
    {
        e.MapGet("/test/authenticated", () => "ok").RequireAuthorization("Authenticated");
        e.MapGet("/test/admin", () => "ok").RequireAuthorization("Admin");
        e.MapGet("/test/internal", () => "ok").RequireAuthorization("Internal");
    }
}

public class SkeletonAuthTests : IDisposable
{
    const string RoleClaim = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";
    readonly BookingFactory f = new();

    public void Dispose() => f.Dispose();

    HttpClient With(string? token)
    {
        var probe = f.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton<IServiceFeature, PolicyProbeFeature>())).CreateClient();
        if (token != null) probe.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return probe;
    }

    [Fact]
    public async Task Token_with_the_wrong_audience_is_401()
    {
        var token = new Microsoft.IdentityModel.JsonWebTokens.JsonWebTokenHandler().CreateToken(new Microsoft.IdentityModel.Tokens.SecurityTokenDescriptor
        {
            Issuer = BookingFactory.Issuer, Audience = "someone-else", Expires = DateTime.UtcNow.AddMinutes(5),
            Claims = new Dictionary<string, object> { ["sub"] = "demo-user-1" },
        });
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/v1/me/availability")).StatusCode);
    }

    [Fact]
    public async Task Authenticated_policy_needs_a_signed_in_user()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await With(null).GetAsync("/test/authenticated")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await With(f.Token("demo-user-1")).GetAsync("/test/authenticated")).StatusCode);
    }

    [Theory]
    [InlineData("Admin", HttpStatusCode.OK)]
    [InlineData("SuperAdmin", HttpStatusCode.OK)]
    [InlineData("User", HttpStatusCode.Forbidden)]
    public async Task Admin_policy_by_role_claim(string role, HttpStatusCode expected) =>
        Assert.Equal(expected, (await With(f.Token("demo-user-1", (RoleClaim, role))).GetAsync("/test/admin")).StatusCode);

    [Fact]
    public async Task Internal_policy_wants_the_internal_scheme_and_a_user_token_is_not_enough() =>
        Assert.NotEqual(HttpStatusCode.OK, (await With(f.Token("demo-user-1", (RoleClaim, "Admin"))).GetAsync("/test/internal")).StatusCode);
}

public class SkeletonHealthTests : IDisposable
{
    readonly BookingFactory f = new();
    public void Dispose() => f.Dispose();

    static async Task<Dictionary<string, string?>> Checks(HttpClient c)
    {
        var r = await c.GetAsync("/health/details");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("checks").EnumerateArray()
            .ToDictionary(c => c.GetProperty("name").GetString()!, c => c.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Health_details_lists_database_identity_and_directory()
    {
        var checks = await Checks(f.CreateClient());
        Assert.Equal(["database", "directory", "identity"], checks.Keys.Order());
        Assert.Equal("Healthy", checks["database"]);
        Assert.Equal("Degraded", checks["identity"]); // no Identity:Authority in the test host
        Assert.Equal("Healthy", checks["directory"]);
    }

    [Fact]
    public async Task Directory_down_is_Degraded_and_health_stays_200()
    {
        f.Directory.Down = true;
        var c = f.CreateClient();
        Assert.Equal("Degraded", (await Checks(c))["directory"]);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/health")).StatusCode);
    }

    [Fact]
    public async Task Directory_answering_500_on_alive_is_Degraded_never_Unhealthy()
    {
        f.Directory.Override = (_, _) => new HttpResponseMessage(HttpStatusCode.InternalServerError);
        var c = f.CreateClient();
        Assert.Equal("Degraded", (await Checks(c))["directory"]);
        var r = await c.GetAsync("/health/details");
        Assert.DoesNotContain("Unhealthy", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public void Program_cs_is_at_most_60_lines()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !System.IO.File.Exists(Path.Combine(dir.FullName, "GabinetyTech.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        Assert.True(System.IO.File.ReadAllLines(Path.Combine(dir!.FullName, "src/Gabinety.Booking/Program.cs")).Length <= 60);
    }
}
