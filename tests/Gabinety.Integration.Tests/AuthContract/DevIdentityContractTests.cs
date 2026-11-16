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
