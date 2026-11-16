using Gabinety.Reputation.Rules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Gabinety.Reputation.Tests.Rules;

sealed class SpyRule(string code, bool flag) : IReviewRule
{
    public int Calls;
    public string Code => code;
    public string? Check(ReviewText input) { Calls++; return flag ? code : null; }
}

public class PublicationDeciderTests
{
    const string Fine = "Rzetelna, spokojna rozmowa i konkretne wskazówki na przyszłość.";

    static ServiceProvider Build(Dictionary<string, string?> config)
    {
        var b = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        b.Configuration.AddInMemoryCollection(config);
        new RulesFeature().Add(b);
        return b.Services.BuildServiceProvider();
    }

    [Fact]
    public void Rules_are_registered_in_the_documented_order()
    {
        using var sp = Build([]);
        Assert.Equal(["contains_link", "contact_details", "blocked_term", "repetition", "too_short_after_trim"],
            sp.GetServices<IReviewRule>().Select(r => r.Code));
    }

    [Fact]
    public void Clean_text_passes() => Assert.IsType<PublicationDecision.Pass>(new PublicationDecider([new SpyRule("a", false)]).Decide(Fine));

    [Fact]
    public void Reasons_accumulate_in_registration_order()
    {
        var d = new PublicationDecider([new SpyRule("first", true), new SpyRule("skipped", false), new SpyRule("third", true)]);
        var held = Assert.IsType<PublicationDecision.Held>(d.Decide(Fine));
        Assert.Equal(["first", "third"], held.Reasons);
    }

    [Fact]
    public void Real_rules_accumulate_several_reasons()
    {
        using var sp = Build(new() { ["Reviews:BlockedTerms:0"] = "oszustwo" });
        var held = Assert.IsType<PublicationDecision.Held>(sp.GetRequiredService<PublicationDecider>()
            .Decide("Oszustwo!!!!!!!!! napisz: demo-user@example.test albo https://example.test/x"));
        Assert.Equal(["contains_link", "contact_details", "blocked_term", "repetition"], held.Reasons);
    }

    [Fact]
    public void Blocked_terms_come_from_configuration()
    {
        const string text = "To zwykłe oszustwo, nic więcej nie napiszę o tym.";
        using var configured = Build(new() { ["Reviews:BlockedTerms:0"] = "oszustwo" });
        Assert.Equal(["blocked_term"], Assert.IsType<PublicationDecision.Held>(configured.GetRequiredService<PublicationDecider>().Decide(text)).Reasons);
        using var empty = Build([]);
        Assert.IsType<PublicationDecision.Pass>(empty.GetRequiredService<PublicationDecider>().Decide(text));
    }

    [Fact]
    public void Code_hosts_come_from_configuration_and_default_to_the_documented_list()
    {
        const string text = "Repozytorium: https://github.com/demo/demo wygląda dobrze.";
        using var def = Build([]);
        Assert.IsType<PublicationDecision.Pass>(def.GetRequiredService<PublicationDecider>().Decide(text));
        using var custom = Build(new() { ["Reviews:CodeHosts:0"] = "example.test" });
        Assert.IsType<PublicationDecision.Held>(custom.GetRequiredService<PublicationDecider>().Decide(text));
    }

    [Fact]
    public void A_repository_link_on_an_allow_listed_host_alone_passes_and_two_urls_are_held()
    {
        using var sp = Build([]);
        var d = sp.GetRequiredService<PublicationDecider>();
        Assert.IsType<PublicationDecision.Pass>(d.Decide("Kod: https://github.com/demo-org/demo-repo — czysty i dobrze opisany."));
        var held = Assert.IsType<PublicationDecision.Held>(d.Decide("Kod: https://github.com/demo-org/a oraz https://github.com/demo-org/b — dobre."));
        Assert.Equal(["contains_link"], held.Reasons);
    }

    [Fact]
    public void Nineteen_characters_after_trimming_fail_validation_and_no_rule_runs()
    {
        var spy = new SpyRule("spy", true);
        var result = new PublicationDecider([spy]).Decide("   " + new string('a', 19) + "   ");
        Assert.IsType<PublicationDecision.Invalid>(result);
        Assert.Equal(0, spy.Calls);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("aaaaaaaaaaaaaaaaaaa", false)] // 19
    [InlineData("aaaaaaaaaaaaaaaaaaaa", true)] // 20
    public void Validator_lower_bound(string? text, bool valid) => Assert.Equal(valid, new ReviewValidator().Validate(text).Count == 0);

    [Fact]
    public void Validator_upper_bound()
    {
        Assert.Empty(new ReviewValidator().Validate(new string('a', 2000)));
        Assert.NotEmpty(new ReviewValidator().Validate(new string('a', 2001)));
        Assert.Empty(new ReviewValidator().Validate("  " + new string('a', 2000) + "  ")); // trimmed first
    }
}
