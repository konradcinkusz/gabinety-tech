using Gabinety.DemoSeeder;
using Microsoft.Extensions.Configuration;

namespace Gabinety.Integration.Tests.Seeder;

public class SeedGuardTests
{
    static IConfiguration Config(params (string Key, string Value)[] pairs) =>
        new ConfigurationBuilder().AddInMemoryCollection(pairs.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value))).Build();

    [Fact]
    public void It_refuses_when_the_profile_is_not_fake_or_development()
    {
        Assert.NotNull(SeedGuard.Refusal(Config(("Backend:Directory", "http://localhost:5101")), iKnowThisIsDemo: false));
        Assert.NotNull(SeedGuard.Refusal(Config(("GABINETY_FAKE", "0"), ("Environment:Kind", "Production")), iKnowThisIsDemo: false));
        Assert.NotNull(SeedGuard.Refusal(Config(("GABINETY_FAKE", "true")), iKnowThisIsDemo: false));
    }

    [Fact]
    public void The_flag_does_not_replace_the_profile_check()
    {
        Assert.NotNull(SeedGuard.Refusal(Config(("Environment:Kind", "Production")), iKnowThisIsDemo: true));
    }

    [Theory]
    [InlineData("GABINETY_FAKE", "1")]
    [InlineData("Environment:Kind", "Development")]
    public void It_allows_local_endpoints_in_the_fake_or_development_profile(string key, string value)
    {
        var c = Config((key, value), ("Backend:Directory", "http://localhost:5101"), ("Identity:Authority", "http://127.0.0.1:5100"));
        Assert.Null(SeedGuard.Refusal(c, iKnowThisIsDemo: false));
    }

    [Fact]
    public void It_refuses_an_endpoint_that_is_not_on_this_machine_unless_the_flag_is_given()
    {
        var c = Config(("GABINETY_FAKE", "1"), ("Backend:Directory", "https://directory.staging.gabinety.example"), ("Identity:Authority", "http://localhost:5100"));
        var why = SeedGuard.Refusal(c, iKnowThisIsDemo: false);
        Assert.NotNull(why);
        Assert.Contains("Backend:Directory", why);
        Assert.Null(SeedGuard.Refusal(c, iKnowThisIsDemo: true));
    }

    [Fact]
    public void A_candidate_list_is_judged_by_its_first_entry()
    {
        var c = Config(("GABINETY_FAKE", "1"), ("Backend:Directory", "http://localhost:5101,https://other.example"));
        Assert.Null(SeedGuard.Refusal(c, iKnowThisIsDemo: false));
    }

    [Fact]
    public async Task Main_refuses_a_non_local_target_with_exit_code_2_before_any_request()
    {
        var code = await SeederApp.Main(["--Backend:Directory=https://directory.staging.gabinety.example", "--Internal:Keys:Directory=x", "--Identity:Authority=https://id.staging.gabinety.example"]);
        Assert.Equal(2, code);
    }

    [Fact]
    public void Missing_configuration_names_the_keys_not_the_values()
    {
        var e = Assert.Throws<SeedException>(() => SeedOptions.From(Config(("Backend:Directory", "http://localhost:5101"))));
        Assert.Contains("Internal:Keys:Directory", e.Message);
        Assert.Contains("Identity:Authority", e.Message);
        Assert.DoesNotContain("localhost", e.Message);
    }
}
