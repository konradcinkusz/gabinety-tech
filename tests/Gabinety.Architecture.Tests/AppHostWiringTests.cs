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
}
