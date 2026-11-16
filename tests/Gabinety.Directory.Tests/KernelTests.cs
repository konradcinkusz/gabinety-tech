using System.Net;
using Gabinety.ServiceDefaults;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Gabinety.Directory.Tests;

public class KernelTests
{
    static async Task<WebApplication> Host(int perMinute = 60)
    {
        var b = WebApplication.CreateBuilder();
        b.WebHost.UseTestServer();
        b.Configuration["Internal:Key"] = "k-test-1234";
        b.Services.AddRateLimiting(perMinute);
        b.Services.AddAuthentication().AddInternalKeyAuthentication();
        b.Services.AddAuthorization(o => o.AddPolicy("internal", p => p.AddAuthenticationSchemes(Security.InternalScheme).RequireAuthenticatedUser()));
        var app = b.Build();
        app.UseOriginGuard();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapPost("/write", () => "ok");
        app.MapGet("/read", () => "ok");
        app.MapGet("/internal/x", () => "secret").RequireAuthorization("internal");
        await app.StartAsync();
        return app;
    }

    static HttpRequestMessage Post(string? origin = null, string? site = null)
    {
        var r = new HttpRequestMessage(HttpMethod.Post, "/write");
        if (origin != null) r.Headers.Add("Origin", origin);
        if (site != null) r.Headers.Add("Sec-Fetch-Site", site);
        return r;
    }

    [Fact]
    public async Task Foreign_origin_on_a_write_is_403_cross_site_request()
    {
        await using var app = await Host();
        var res = await app.GetTestClient().SendAsync(Post(origin: "https://evil.example"));
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Contains("cross_site_request", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Sec_Fetch_Site_cross_site_on_a_write_is_403()
    {
        await using var app = await Host();
        Assert.Equal(HttpStatusCode.Forbidden, (await app.GetTestClient().SendAsync(Post(site: "cross-site"))).StatusCode);
    }

    [Fact]
    public async Task Same_origin_and_origin_less_writes_pass_and_reads_are_never_guarded()
    {
        await using var app = await Host();
        var c = app.GetTestClient();
        Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(Post(origin: "http://localhost"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(Post())).StatusCode);
        var read = new HttpRequestMessage(HttpMethod.Get, "/read");
        read.Headers.Add("Origin", "https://evil.example");
        Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(read)).StatusCode);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("wrong", HttpStatusCode.Unauthorized)]
    [InlineData("k-test-1234", HttpStatusCode.OK)]
    public async Task Internal_endpoint_needs_the_exact_key(string? key, HttpStatusCode expected)
    {
        await using var app = await Host();
        var r = new HttpRequestMessage(HttpMethod.Get, "/internal/x");
        if (key != null) r.Headers.Add("X-Internal-Key", key);
        Assert.Equal(expected, (await app.GetTestClient().SendAsync(r)).StatusCode);
    }

    [Fact]
    public async Task Rate_limit_answers_429_after_the_permit_is_spent()
    {
        await using var app = await Host(perMinute: 3);
        var c = app.GetTestClient();
        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++) codes.Add((await c.GetAsync("/read")).StatusCode);
        Assert.Equal(3, codes.Count(x => x == HttpStatusCode.OK));
        Assert.Equal(2, codes.Count(x => x == HttpStatusCode.TooManyRequests));
    }
}
