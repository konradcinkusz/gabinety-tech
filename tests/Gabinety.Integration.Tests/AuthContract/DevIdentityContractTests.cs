using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Gabinety.Integration.Tests.AuthContract;

/// <summary>DevIdentity hosted in process. The guard needs GABINETY_FAKE=1, exactly as the AppHost sets it.</summary>
public sealed class DevIdentityHost : WebApplicationFactory<Program>
{
    public DevIdentityHost() => Environment.SetEnvironmentVariable("GABINETY_FAKE", "1");
}

/// <summary>Unit tier: the contract against DevIdentity, in process.</summary>
public class DevIdentityContractTests(DevIdentityHost host) : AuthContractSuite(host.CreateClient()), IClassFixture<DevIdentityHost>;

/// <summary>Integration tier: the same contract against the real authservice image. The supervisor sets AUTH_BASE_URL; unset, every test is reported as skipped.</summary>
[Trait("tier", "integration")]
[RequiresEnv("AUTH_BASE_URL")]
public class RealAuthServiceContractTests() : AuthContractSuite(ForBaseUrl(Environment.GetEnvironmentVariable("AUTH_BASE_URL") is { Length: > 0 } url ? url : "http://unset.invalid"));

/// <summary>F-02 guard: every authservice route the web calls (web/src/lib/session/identity.ts) exists in the stand-in, with and without the /api/v1 alias, and none answers 404.</summary>
public class DevIdentityRouteSetTests(DevIdentityHost host) : IClassFixture<DevIdentityHost>
{
    public static IEnumerable<object[]> Routes() =>
        new (string Method, string Path)[]
        {
            ("GET", "auth/me"), ("GET", "auth/consents"), ("POST", "auth/consents"), ("GET", "auth/consents/versions"), ("GET", "auth/export"), ("DELETE", "auth/account"),
            ("POST", "auth/register"), ("POST", "auth/login"), ("POST", "auth/refresh"), ("POST", "auth/logout"),
            ("GET", "external-auth/providers"), ("GET", "external-auth/login"), ("POST", "external-auth/exchange"),
        }.SelectMany(r => new[] { "/api/v1/", "/api/" }.Select(prefix => new object[] { r.Method, prefix + r.Path }));

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task The_route_the_web_calls_is_mapped(string method, string path)
    {
        using var http = host.CreateClient(new() { AllowAutoRedirect = false });
        using var response = await http.SendAsync(new HttpRequestMessage(new HttpMethod(method), path) { Content = method == "GET" ? null : JsonContent.Create(new { }) });
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }
}

/// <summary>What only the stand-in promises: the seeded synthetic users of SPEC 6.7 and the role claim of the admin.</summary>
public class DevIdentitySeedTests(DevIdentityHost host) : AuthContractSuite(host.CreateClient()), IClassFixture<DevIdentityHost>
{
    // Fixed development value stated in tools/Gabinety.DevIdentity/README.md; not a secret.
    private const string SeedPassword = "Demo-Dev-Passw0rd!";

    public static IEnumerable<object[]> SeededNames() =>
        new[] { "demo-client", "demo-admin" }.Concat(Enumerable.Range(1, 8).Select(i => $"demo-practitioner-{i}")).Select(n => new object[] { n });

    private async Task<ParsedJwt> LoginTokenAsync(string name)
    {
        var response = await LoginAsync($"{name}@demo.example", SeedPassword);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return ParsedJwt.Parse((await ReadJson(response)).GetProperty("accessToken").GetString()!);
    }

    [Theory]
    [MemberData(nameof(SeededNames))]
    public async Task Every_seeded_user_can_log_in(string name)
    {
        var jwt = await LoginTokenAsync(name);
        Assert.Equal($"{name}@demo.example", jwt.Payload.GetProperty("email").GetString());
        Assert.False(string.IsNullOrWhiteSpace(jwt.Payload.GetProperty("sub").GetString()));
    }

    [Fact]
    public async Task Admin_carries_the_platform_role_Admin_under_the_long_claim_name()
    {
        var jwt = await LoginTokenAsync("demo-admin");
        Assert.Equal("Admin", jwt.Payload.GetProperty(RoleClaim).GetString());
        Assert.False(jwt.Payload.TryGetProperty("role", out _));
        Assert.Equal(OrdinaryClaims.Append(RoleClaim).Order(StringComparer.Ordinal), ClaimNames(jwt));
    }

    [Theory]
    [InlineData("demo-client")]
    [InlineData("demo-practitioner-1")]
    public async Task Other_seeded_users_carry_no_role(string name)
    {
        var jwt = await LoginTokenAsync(name);
        Assert.Equal(OrdinaryClaims.Order(StringComparer.Ordinal), ClaimNames(jwt));
    }

    [Fact]
    public async Task Tokens_have_no_iat_nbf_or_email_verified_as_the_real_service()
    {
        var jwt = await LoginTokenAsync("demo-client");
        Assert.False(jwt.Payload.TryGetProperty("iat", out _));
        Assert.False(jwt.Payload.TryGetProperty("nbf", out _));
        Assert.False(jwt.Payload.TryGetProperty("email_verified", out _));
    }

    // T-08.02: the Booking and Reputation demo reset keys on a user id starting with "demo-" (CONTRACTS 10.4), so the stand-in issues such subs for demo users.
    [Theory]
    [MemberData(nameof(SeededNames))]
    public async Task A_seeded_demo_user_has_the_user_name_as_its_sub(string name)
    {
        var jwt = await LoginTokenAsync(name);
        Assert.Equal(name, jwt.Payload.GetProperty("sub").GetString());
    }

    [Fact]
    public async Task A_demo_user_registered_later_gets_a_demo_sub_and_me_agrees()
    {
        var tokens = await RegisterAsync("demo-client-7@demo.example");
        var access = tokens.GetProperty("accessToken").GetString()!;
        Assert.Equal("demo-client-7", ParsedJwt.Parse(access).Payload.GetProperty("sub").GetString());
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/v1/auth/me");
        request.Headers.Authorization = new("Bearer", access);
        Assert.Equal("demo-client-7", (await ReadJson(await Http.SendAsync(request))).GetProperty("id").GetString());
    }

    [Theory]
    [InlineData("someone-else@demo.example")]
    [InlineData("demo-sneaky@example.com")]
    public async Task Any_other_user_keeps_an_opaque_sub_outside_the_demo_namespace(string email)
    {
        var tokens = await RegisterAsync(email);
        var sub = ParsedJwt.Parse(tokens.GetProperty("accessToken").GetString()!).Payload.GetProperty("sub").GetString()!;
        Assert.DoesNotContain("demo-", sub);
        Assert.True(Guid.TryParseExact(sub, "N", out _));
    }

    [Fact]
    public async Task Health_stays_up()
    {
        Assert.Equal(HttpStatusCode.OK, (await Http.GetAsync("health")).StatusCode);
    }
}

/// <summary>ADR-0003: DevIdentity refuses to start unless GABINETY_FAKE=1, and says why.</summary>
public class DevIdentityStartupTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("true")]
    [InlineData("")]
    public async Task Without_GABINETY_FAKE_equal_to_1_it_exits_non_zero_and_says_why(string? value)
    {
        using var process = DevIdentityProcess.Start(DevIdentityProcess.FreeUrl(), value);
        var stderr = process.StandardError.ReadToEndAsync();
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(limit.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); Assert.Fail("DevIdentity kept running without GABINETY_FAKE=1"); }

        Assert.NotEqual(0, process.ExitCode);
        Assert.Contains("GABINETY_FAKE=1", await stderr);
    }
}
