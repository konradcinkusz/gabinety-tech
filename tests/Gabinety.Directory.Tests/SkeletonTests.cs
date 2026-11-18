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
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Gabinety.Directory.Tests;

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

/// <summary>Directory host with an in-memory RSA key as its "JWKS" (generated per run) and the audience "directory-test".</summary>
public sealed class SkeletonFactory : WebApplicationFactory<Program>
{
    public const string Audience = "directory-test";
    public const string Issuer = "https://issuer.demo.invalid";
    readonly RSA _rsa = RSA.Create(2048);

    public string Token(string audience = Audience, RSA? key = null, params (string Type, string Value)[] claims)
    {
        var d = new SecurityTokenDescriptor
        {
            Issuer = Issuer, Audience = audience, Expires = DateTime.UtcNow.AddMinutes(5),
            Claims = new Dictionary<string, object> { ["sub"] = "demo-user-1" },
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(key ?? _rsa) { KeyId = "k1" }, SecurityAlgorithms.RsaSha256),
        };
        foreach (var g in claims.GroupBy(c => c.Type))
            d.Claims[g.Key] = g.Count() == 1 ? g.First().Value : g.Select(c => c.Value).ToArray();
        return new JsonWebTokenHandler().CreateToken(d);
    }

    public HttpClient ClientWith(string? token)
    {
        var c = CreateClient();
        if (token != null) c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Identity:Audience", Audience);
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
    const string RoleClaim = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";

    [Fact]
    public async Task Protected_endpoint_without_a_token_is_401() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.ClientWith(null).GetAsync("/api/v1/me")).StatusCode);

    [Fact]
    public async Task Token_with_wrong_audience_is_401() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.ClientWith(f.Token("someone-else")).GetAsync("/api/v1/me")).StatusCode);

    [Fact]
    public async Task Token_with_right_audience_and_key_is_accepted() =>
        Assert.Equal(HttpStatusCode.OK, (await f.ClientWith(f.Token()).GetAsync("/api/v1/me")).StatusCode);

    [Fact]
    public async Task Token_signed_with_a_foreign_key_is_401()
    {
        using var other = RSA.Create(2048);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.ClientWith(f.Token(key: other)).GetAsync("/api/v1/me")).StatusCode);
    }

    [Fact]
    public async Task Authenticated_policy_needs_a_signed_in_user()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.ClientWith(null).GetAsync("/test/authenticated")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await f.ClientWith(f.Token()).GetAsync("/test/authenticated")).StatusCode);
    }

    [Theory]
    [InlineData("Admin", HttpStatusCode.OK)]
    [InlineData("SuperAdmin", HttpStatusCode.OK)]
    [InlineData("User", HttpStatusCode.Forbidden)]
    public async Task Admin_policy_by_role_claim(string role, HttpStatusCode expected) =>
        Assert.Equal(expected, (await f.ClientWith(f.Token(claims: (RoleClaim, role))).GetAsync("/test/admin")).StatusCode);

    [Fact]
    public async Task Admin_policy_without_any_role_is_403() =>
        Assert.Equal(HttpStatusCode.Forbidden, (await f.ClientWith(f.Token()).GetAsync("/test/admin")).StatusCode);

    [Fact]
    public async Task Internal_policy_wants_the_internal_scheme_and_a_user_token_is_not_enough() =>
        Assert.NotEqual(HttpStatusCode.OK, (await f.ClientWith(f.Token(claims: (RoleClaim, "Admin"))).GetAsync("/test/internal")).StatusCode);
}

public class SkeletonHealthTests(SkeletonFactory f) : IClassFixture<SkeletonFactory>
{
    [Fact]
    public async Task Health_is_200()
    {
        var r = await f.CreateClient().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task Health_details_lists_database_and_identity_and_identity_is_degraded_without_authority()
    {
        var r = await f.CreateClient().GetAsync("/health/details");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        var checks = doc.RootElement.GetProperty("checks").EnumerateArray()
            .ToDictionary(c => c.GetProperty("name").GetString()!, c => c.GetProperty("status").GetString());
        Assert.Equal("Healthy", checks["database"]);
        Assert.Equal("Degraded", checks["identity"]);
    }

    [Fact]
    public void Program_cs_is_at_most_60_lines()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !System.IO.File.Exists(Path.Combine(dir.FullName, "GabinetyTech.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        Assert.True(System.IO.File.ReadAllLines(Path.Combine(dir!.FullName, "src/Gabinety.Directory/Program.cs")).Length <= 60);
    }
}
