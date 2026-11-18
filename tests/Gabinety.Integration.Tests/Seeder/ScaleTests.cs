using System.Net.Http.Json;
using Gabinety.Integration.Tests.AuthContract;
using Gabinety.DemoSeeder;
using Microsoft.Extensions.Configuration;

namespace Gabinety.Integration.Tests.Seeder;

public class ScalePlanTests
{
    [Fact]
    public void Factor_1_adds_nothing()
    {
        Assert.Empty(ScalePlan.Extras(1));
        Assert.Equal(DemoCatalog.Practitioners.Length, ScalePlan.TotalPractitioners(1));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(20)]
    [InlineData(200)] // SPEC 13: 2000 practitioners and 20000 reviews
    public void Counts_follow_the_factor_exactly(int factor)
    {
        var extras = ScalePlan.Extras(factor);
        Assert.Equal(10 * factor, DemoCatalog.Practitioners.Length + extras.Count);
        var reviews = DemoCatalog.Practitioners.Where(p => p.Stats is not null).Sum(p => p.Stats!.Reviews) + extras.Sum(e => e.Stats.Reviews);
        Assert.Equal(100 * factor, reviews);
    }

    [Fact]
    public void Every_extra_is_in_the_demo_namespace_and_unique()
    {
        var extras = ScalePlan.Extras(200);
        Assert.All(extras, e => { Assert.True(DemoNames.IsDemo(e.UserName)); Assert.True(DemoNames.IsDemo(e.Slug)); });
        Assert.Equal(extras.Count, extras.Select(e => e.Slug).Distinct().Count());
        Assert.Equal(extras.Count, extras.Select(e => e.Nip).Distinct().Count());
        var catalogNips = DemoCatalog.Practitioners.Select(p => p.Nip).ToHashSet();
        Assert.DoesNotContain(extras, e => catalogNips.Contains(e.Nip));
        Assert.Equal(11, extras[0].Number); // the catalog uses 1 to 10
    }

    [Fact]
    public void Texts_carry_no_digit_runs_or_contact_data_and_the_plan_is_deterministic()
    {
        var a = ScalePlan.Extras(20); var b = ScalePlan.Extras(20);
        Assert.Equal(a.Select(x => x.Bio), b.Select(x => x.Bio));
        Assert.All(a, e => { Assert.DoesNotMatch(@"\d", e.Bio + e.Headline); Assert.DoesNotContain("@", e.Bio + e.Headline); });
        Assert.All(a, e => Assert.InRange(e.Stats.Average, 3.5m, 5.0m));
    }

    [Theory]
    [InlineData("200", 200, true)]
    [InlineData("0", 1, false)]
    [InlineData("many", 1, false)]
    [InlineData("5000", 1, false)]
    public void The_factor_comes_from_configuration_and_a_bad_value_is_refused(string value, int expected, bool ok)
    {
        var c = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Backend:Directory"] = "http://localhost:5101", ["Internal:Keys:Directory"] = "k", ["Identity:Authority"] = "http://localhost:5100", ["Seed:Factor"] = value }).Build();
        if (ok) Assert.Equal(expected, SeedOptions.From(c).Factor);
        else Assert.Throws<SeedException>(() => SeedOptions.From(c));
    }

    [Theory]
    [InlineData("--factor", "200", "--Seed:Factor=200")]
    [InlineData("--factor=7", null, "--Seed:Factor=7")]
    public void The_factor_flag_becomes_a_configuration_key(string first, string? second, string expected)
    {
        var args = second is null ? new[] { first } : new[] { first, second };
        Assert.Equal([expected], SeederApp.ParseMode(args).Remaining);
    }
}

/// <summary>The step against a real in-process Directory: a small factor, then a second run that changes nothing.</summary>
public class ScaleSeedStepTests : IAsyncLifetime
{
    readonly DevIdentityHost _identity = new();
    DirectoryHost _directory = null!;

    public Task InitializeAsync() { _directory = new DirectoryHost(_identity); return Task.CompletedTask; }
    public Task DisposeAsync() { _directory.Dispose(); _identity.Dispose(); return Task.CompletedTask; }

    [Fact]
    public async Task Factor_3_makes_30_practitioners_and_a_second_run_writes_nothing_new()
    {
        var options = new SeedOptions("http://localhost", DirectoryHost.Key, DirectoryHost.Authority, SeedOptions.DevIdentityPassword) { Factor = 3 };
        var http = new TestSeedHttp(_identity, _directory);
        var log = new CapturedLog();
        Assert.Equal(0, await SeederApp.RunAsync(options, http, log, TimeProvider.System, CancellationToken.None));
        Assert.Contains("scale: factor 3: 20 extra practitioners, 300 reviews in all", log.Text);

        using var client = _directory.CreateClient();
        var page = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/v1/sitemap");
        var approved = page.GetProperty("practitioners").GetArrayLength();
        Assert.Equal(6 + 20, approved); // the six approved of the catalog and the twenty extras

        var again = new CapturedLog();
        Assert.Equal(0, await SeederApp.RunAsync(options, new TestSeedHttp(_identity, _directory), again, TimeProvider.System, CancellationToken.None));
        Assert.Contains("scale: done, 0 changes", again.Text);
    }
}
