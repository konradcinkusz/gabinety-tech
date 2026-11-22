using System.Text.RegularExpressions;

namespace Gabinety.Architecture.Tests;

/// <summary>
/// T-08.02: what the AppHost hands the services and the seeder (CONTRACTS 10.3, 10.4). The AppHost cannot be started in a unit test, so the composition root is read as text;
/// the real check is scripts/check-fake-profile.sh against a running fake profile.
/// </summary>
public class AppHostWiringTests
{
    static readonly string Program = File.ReadAllText(Path.Combine(Repo.Root, "src/Gabinety.AppHost/Program.cs"));

    /// <summary>The body of the local function <c>Service&lt;TProject&gt;</c>: what every service gets.</summary>
    static string ServiceHelper() => Regex.Match(Program, @"IResourceBuilder<ProjectResource> Service<TProject>.*?\n}\n", RegexOptions.Singleline).Value;

    /// <summary>The body of the local function <c>Seeder</c>.</summary>
    static string SeederHelper() => Regex.Match(Program, @"IResourceBuilder<ProjectResource> Seeder\(.*?\n}\n", RegexOptions.Singleline).Value;

    [Theory]
    [InlineData("Environment__Kind")] // maps the clock and reset endpoints (CONTRACTS 10.4)
    [InlineData("Internal__Key\"")]
    [InlineData("Internal__Keys__Directory")]
    [InlineData("Internal__Keys__Booking")]
    [InlineData("Internal__Keys__Reputation")]
    [InlineData("Internal__Keys__Assistant")]
    [InlineData("Backend__Directory")]
    [InlineData("Backend__Booking")]
    [InlineData("Backend__Reputation")]
    [InlineData("Backend__Assistant")]
    public void Every_service_gets(string setting) => Assert.Contains(setting, ServiceHelper());

    [Fact]
    public void The_fake_profile_still_sets_the_fake_flag_for_every_service() => Assert.Contains("\"GABINETY_FAKE\", \"1\"", ServiceHelper());

    [Fact]
    public void The_internal_key_is_an_AppHost_parameter_with_a_development_value_only_in_the_fake_profile()
    {
        var m = Regex.Match(Program, @"var internalKey = fake\s*\?\s*builder\.AddParameter\(""internal-key"", \(\) => ""([^""]+)"", secret: true\)\s*:\s*builder\.AddParameter\(""internal-key"", secret: true\);");
        Assert.True(m.Success, "internal-key: a development default in the fake profile, none (asked for) in the default one");
        Assert.StartsWith("demo-", m.Groups[1].Value);
    }

    [Fact]
    public void The_seeder_starts_by_itself_only_in_the_fake_profile_and_after_the_services_are_healthy()
    {
        var seeder = SeederHelper();
        Assert.Contains("AddProject<Projects.Gabinety_DemoSeeder>", seeder);
        foreach (var service in new[] { "directory", "booking", "reputation" }) Assert.Contains($".WaitFor({service})", seeder);
        Assert.Contains("WaitFor(identity)", seeder);
        Assert.Contains("fake && name == \"seeder\" ? seeder : seeder.WithExplicitStart()", seeder);
        foreach (var setting in new[] { "Backend__Directory", "Backend__Booking", "Backend__Reputation", "Internal__Keys__Directory", "Internal__Keys__Booking", "Internal__Keys__Reputation", "Identity__Authority", "Environment__Kind" })
            Assert.Contains(setting, seeder);
    }

    [Fact]
    public void A_second_seeder_resource_resets_and_seeds_again_on_request() => Assert.Contains("Seeder(\"seeder-reset\", \"--reset\");", Program);

    [Fact]
    public void The_rate_limit_is_raised_in_the_fake_profile_only()
    {
        var service = ServiceHelper();
        var fakeBlock = Regex.Match(service, @"if \(fake\)\s*\{.*?\n    \}\n", RegexOptions.Singleline).Value;
        var m = Regex.Match(fakeBlock, @"WithEnvironment\(""RateLimit__PerMinute"", ""(\d+)""\)");
        Assert.True(m.Success, "the fake profile block must set RateLimit__PerMinute");
        Assert.True(int.Parse(m.Groups[1].Value) >= 1000);
        Assert.Single(Regex.Matches(Program, "RateLimit__PerMinute")); // never in the default profile, never anywhere else
    }

    [Theory]
    [InlineData("Directory"), InlineData("Booking"), InlineData("Reputation"), InlineData("Assistant")]
    public void Every_service_reads_its_rate_limit_from_configuration(string service)
    {
        var program = File.ReadAllText(Path.Combine(Repo.Root, $"src/Gabinety.{service}/Program.cs"));
        Assert.Contains("AddRateLimiting(builder.Configuration)", program);
    }

    // ---- the Blazor web host, the only frontend since the cutover (ADR-0010) ----

    /// <summary>The statement that adds the Blazor host: from its AddProject to the end of the chain.</summary>
    static string BlazorWeb() => Regex.Match(Program, @"AddProject<Projects\.Gabinety_Web>\(""web-blazor"".*?;\n", RegexOptions.Singleline).Value;

    [Fact]
    public void The_Blazor_host_is_the_web_resource_on_3000_with_health_and_the_backend_addresses()
    {
        var web = BlazorWeb();
        Assert.NotEmpty(web);
        Assert.Contains("WithHttpEndpoint(port: 3000", web);
        Assert.Contains("WithHttpHealthCheck(\"/health\")", web);
        foreach (var setting in new[] { "Backend__Directory", "Backend__Booking", "Backend__Reputation", "Backend__Assistant", "Backend__Auth", "Site__BaseUrl" }) Assert.Contains(setting, web);
        Assert.Contains("\"Site__BaseUrl\", \"http://localhost:3000\"", web);
        Assert.Contains("WaitFor(directory)", web);
        // found by running it in a browser: without this the host runs as Production from an unpublished build and its compressed static assets answer 500
        Assert.Contains("WithEnvironment(\"ASPNETCORE_ENVIRONMENT\", \"Development\")", web);
    }

    [Fact]
    public void There_is_no_JavaScript_app_resource_any_more()
    {
        Assert.DoesNotContain("AddJavaScriptApp", Program);
        Assert.DoesNotContain("WithPnpm", Program);
        Assert.False(System.IO.Directory.Exists(Path.Combine(Repo.Root, "web")), "web/ (Next.js) was retired by the cutover");
    }

    [Fact]
    public void The_Blazor_host_gets_the_same_backend_addresses_as_the_services_and_no_internal_key()
    {
        var web = BlazorWeb();
        // the identity provider is the one every backend is given; the internal keys (ADR-0007) are for services calling services, never for a frontend
        Assert.Contains("identityAuthority", web);
        Assert.DoesNotContain("Internal__Key", web);
        Assert.DoesNotContain("internalKey", web);
    }

    [Fact]
    public void The_fake_profile_check_waits_for_the_one_frontend()
    {
        var script = File.ReadAllText(Path.Combine(Repo.Root, "scripts/check-fake-profile.sh"));
        Assert.Contains("http://localhost:3000/health", script);
        Assert.DoesNotContain("3001", script);
        Assert.DoesNotContain("/api/health", script);
    }
}
