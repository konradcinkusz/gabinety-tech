using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Gabinety.ServiceDefaults;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Gabinety.Assistant.Tests;

/// <summary>Assistant has no authenticated endpoint yet, so the test host adds a protected one behind the product's policy.</summary>
sealed class PolicyProbeFeature : IServiceFeature
{
    public void Add(IHostApplicationBuilder builder) { }
    public void Map(IEndpointRouteBuilder e) => e.MapGet("/test/authenticated", () => "ok").RequireAuthorization("Authenticated");
}

/// <summary>Assistant host with an in-memory RSA key as its "JWKS" (generated per run) and the audience "assistant-test".</summary>
public sealed class SkeletonFactory : WebApplicationFactory<Program>
{
    public const string Audience = "assistant-test";
    public const string Issuer = "https://issuer.demo.invalid";
    readonly RSA _rsa = RSA.Create(2048);

    public string Token(string audience = Audience, RSA? key = null) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        Issuer = Issuer, Audience = audience, Expires = DateTime.UtcNow.AddMinutes(5),
        Claims = new Dictionary<string, object> { ["sub"] = "demo-user-1" },
        SigningCredentials = new SigningCredentials(new RsaSecurityKey(key ?? _rsa) { KeyId = "k1" }, SecurityAlgorithms.RsaSha256),
    });

    public HttpClient ClientWith(string? token)
    {
        var c = CreateClient();
        if (token != null) c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Identity:Audience", Audience);
        builder.UseSetting("Llm:Provider", "Fake");
        builder.ConfigureServices(s =>
        {
            s.AddSingleton<IServiceFeature, PolicyProbeFeature>();
            s.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
            {
                var cfg = new OpenIdConnectConfiguration { Issuer = Issuer };
                cfg.SigningKeys.Add(new RsaSecurityKey(_rsa) { KeyId = "k1" });
                o.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(cfg);
            });
        });
    }

    protected override void Dispose(bool disposing) { if (disposing) _rsa.Dispose(); base.Dispose(disposing); }
}

public class SkeletonAuthTests(SkeletonFactory f) : IClassFixture<SkeletonFactory>
{
    [Fact]
    public async Task Protected_endpoint_without_a_token_is_401() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.ClientWith(null).GetAsync("/test/authenticated")).StatusCode);

    [Fact]
    public async Task Token_with_wrong_audience_is_401_and_the_right_one_is_accepted()
    {
        Assert.Equal(HttpStatusCode.OK, (await f.ClientWith(f.Token()).GetAsync("/test/authenticated")).StatusCode); // positive control
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.ClientWith(f.Token("someone-else")).GetAsync("/test/authenticated")).StatusCode);
    }

    [Fact]
    public async Task Token_signed_with_a_foreign_key_is_401()
    {
        using var other = RSA.Create(2048);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.ClientWith(f.Token(key: other)).GetAsync("/test/authenticated")).StatusCode);
    }
}

public class SkeletonHealthTests(SkeletonFactory f) : IClassFixture<SkeletonFactory>
{
    [Fact]
    public async Task Health_is_200() => Assert.Equal(HttpStatusCode.OK, (await f.CreateClient().GetAsync("/health")).StatusCode);

    [Fact]
    public async Task Health_details_lists_database_identity_and_llm_and_never_reports_Unhealthy()
    {
        var r = await f.CreateClient().GetAsync("/health/details");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        var checks = doc.RootElement.GetProperty("checks").EnumerateArray()
            .ToDictionary(c => c.GetProperty("name").GetString()!, c => c.GetProperty("status").GetString());
        Assert.Equal("Healthy", checks["database"]);
        Assert.Equal("Degraded", checks["identity"]);
        Assert.Equal("Healthy", checks["llm"]); // fake gateway
        Assert.DoesNotContain("Unhealthy", checks.Values);
    }

    [Fact]
    public void Program_cs_is_at_most_60_lines()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !System.IO.File.Exists(Path.Combine(dir.FullName, "GabinetyTech.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        Assert.True(System.IO.File.ReadAllLines(Path.Combine(dir!.FullName, "src/Gabinety.Assistant/Program.cs")).Length <= 60);
    }

    [Fact]
    public async Task Status_endpoint_is_public()
    {
        var r = await f.CreateClient().GetAsync("/api/v1/assistant/status");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("\"Fake\"", await r.Content.ReadAsStringAsync());
    }
}

public class SkeletonLlmHealthTests
{
    [Theory]
    [InlineData(Gabinety.Llm.LlmGatewayState.Unavailable)]
    [InlineData(Gabinety.Llm.LlmGatewayState.NotAuthenticated)]
    public async Task An_unavailable_gateway_makes_the_llm_check_Degraded_never_Unhealthy_and_health_stays_200(Gabinety.Llm.LlmGatewayState state)
    {
        using var f = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Llm:Provider", "Fake");
            b.ConfigureServices(s =>
            {
                Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.RemoveAll<Gabinety.Llm.ILlmGateway>(s);
                s.AddSingleton<Gabinety.Llm.ILlmGateway>(new Policy.CountingGateway(state, []));
            });
        });
        var c = f.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/health")).StatusCode);
        using var doc = JsonDocument.Parse(await c.GetStringAsync("/health/details"));
        var checks = doc.RootElement.GetProperty("checks").EnumerateArray()
            .ToDictionary(x => x.GetProperty("name").GetString()!, x => x.GetProperty("status").GetString());
        Assert.Equal("Degraded", checks["llm"]);
        Assert.DoesNotContain("Unhealthy", checks.Values);
    }
}
