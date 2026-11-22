using System.Net;
using Gabinety.Web.Api;

namespace Gabinety.Web.Tests.Public;

/// <summary>Contract availability on the public pages (ADR proposal 0013): the profile, the card and the filter of the list. The clock stands still: 2026-11-23, 11:00 in Warsaw.</summary>
public class ContractAvailabilityPagesTests
{
    const string Slug = "/gabinet/demo-anna-demowska";

    static PractitionerProfileDto WithContract(ContractAvailabilityDto? contract) => PublicSupport.Profile() with { Contract = contract };

    static Func<HttpRequestMessage, HttpResponseMessage> Directory(ContractAvailabilityDto? contract) => PublicSupport.DefaultDirectory(WithContract(contract));

    static string Plain(string s) => s.Replace(' ', ' ');

    [Fact]
    public async Task A_profile_that_takes_proposals_says_when_and_offers_the_two_buttons()
    {
        await using var web = PublicSupport.Host(Directory(new ContractAvailabilityDto(new DateOnly(2027, 2, 23), 20, new MoneyDto(11000, "PLN"))));
        var page = await web.NewBrowser().GetAsync(Slug);
        Assert.Equal(HttpStatusCode.OK, page.Status);
        var section = page.One("[data-testid=contract-availability]")!;
        Assert.Equal("Współpraca kontraktowa", section.QuerySelector("h2")!.TextContent);
        Assert.Equal("Dostępny do współpracy za 3 miesiące", section.QuerySelector("strong")!.TextContent);
        Assert.Equal("20 godz. tygodniowo · Orientacyjna stawka: 110,00 zł za godzinę", Plain(Page.Squash(section.QuerySelector("p.muted")!.TextContent)));
        var (shortLink, longLink) = (page.One("[data-testid=propose-short]")!, page.One("[data-testid=propose-long]")!);
        Assert.Equal(("Zaproponuj współpracę", "/propozycja/demo-anna-demowska?kind=short"), (shortLink.TextContent, shortLink.GetAttribute("href")));
        Assert.Equal(("Zaproponuj współpracę długoterminową", "/propozycja/demo-anna-demowska"), (longLink.TextContent, longLink.GetAttribute("href")));
        Assert.All(new[] { shortLink, longLink }, l => { Assert.Equal("a", l.TagName.ToLowerInvariant()); Assert.DoesNotContain("btn--primary", l.ClassName); }); // the offers keep the one main action
        Assert.Equal(["O mnie", "Oferta", "Opinie"], page.All("h2").Select(h => h.TextContent).Where(t => t != "Współpraca kontraktowa"));
    }

    [Fact]
    public async Task Available_now_has_no_date_and_no_rate_line_without_a_rate()
    {
        await using var web = PublicSupport.Host(Directory(new ContractAvailabilityDto(null, 8, null)));
        var page = await web.NewBrowser().GetAsync(Slug);
        Assert.Equal("Dostępny do współpracy od zaraz", page.TextOf("[data-testid=contract-availability] strong"));
        Assert.Equal("8 godz. tygodniowo", Page.Squash(page.TextOf("[data-testid=contract-availability] p.muted")));
    }

    [Fact]
    public async Task A_profile_that_does_not_take_proposals_has_no_section_and_no_buttons()
    {
        await using var web = PublicSupport.Host(Directory(null));
        var page = await web.NewBrowser().GetAsync(Slug);
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Null(page.One("[data-testid=contract-availability]"));
        Assert.Null(page.One("[data-testid=propose-short]"));
        Assert.DoesNotContain("Zaproponuj współpracę", page.Html);
        Assert.Equal(["O mnie", "Oferta", "Opinie"], page.All("h2").Select(h => h.TextContent)); // the page is what it was
    }

    [Fact]
    public async Task The_structured_data_and_the_index_rules_are_unchanged_by_the_section()
    {
        await using var web = PublicSupport.Host(Directory(new ContractAvailabilityDto(null, 20, null)));
        var page = await web.NewBrowser().GetAsync(Slug);
        Assert.Single(page.All("script[type='application/ld+json']"));
        Assert.Equal("index,follow", page.One("meta[name=robots]")!.GetAttribute("content"));
    }

    static WebFactory ListHost(params PractitionerCardDto[] cards) => PublicSupport.Host(r => r.RequestUri!.AbsolutePath == "/api/v1/practitioners"
        ? FakeBackend.Json(new PageDto<PractitionerCardDto>(cards, null)) : PublicSupport.DefaultDirectory()(r));

    [Fact]
    public async Task A_card_says_in_one_badge_when_the_programmer_is_available_for_a_contract()
    {
        await using var web = ListHost(
            Sample.Card("demo-now", "Osoba Teraz", contract: new ContractAvailabilityDto(null, 20, null)),
            Sample.Card("demo-later", "Osoba Później", contract: new ContractAvailabilityDto(new DateOnly(2027, 2, 23), 10, null)),
            Sample.Card("demo-none", "Osoba Bez"));
        var page = await web.NewBrowser().GetAsync("/programisci");
        var cards = page.All("[data-testid=practitioner-card]");
        Assert.Equal(3, cards.Count);
        Assert.Equal("Dostępny do kontraktu", cards[0].QuerySelector("[data-testid=card-contract]")!.TextContent);
        Assert.Equal("Kontrakt: za 3 miesiące", cards[1].QuerySelector("[data-testid=card-contract]")!.TextContent);
        Assert.Null(cards[2].QuerySelector("[data-testid=card-contract]"));
        Assert.All(cards.Take(2), c => Assert.Contains("badge--info", c.QuerySelector("[data-testid=card-contract]")!.ClassName));
    }

    [Fact]
    public async Task The_contract_filter_is_a_plain_link_passed_to_the_directory_and_marked_current()
    {
        await using var web = ListHost(Sample.Card("demo-now", "Osoba Teraz", contract: new ContractAvailabilityDto(null, 20, null)));
        var plain = await web.NewBrowser().GetAsync("/programisci");
        var group = plain.All("nav[aria-label=Filtry] section.filters__group").Last();
        Assert.Equal("Współpraca", group.QuerySelector("h2")!.TextContent);
        Assert.Equal(["Wszyscy", "Dostępni do kontraktu"], group.QuerySelectorAll("a").Select(a => a.TextContent));
        Assert.Equal(["/programisci", "/programisci?contract=1"], group.QuerySelectorAll("a").Select(a => a.GetAttribute("href")));
        Assert.Equal(["Wszyscy"], group.QuerySelectorAll("a[aria-current=true]").Select(a => a.TextContent));
        Assert.DoesNotContain("contract", web.Directory.Requests.Last(r => r.PathAndQuery.StartsWith("/api/v1/practitioners")).PathAndQuery);

        var filtered = await web.NewBrowser().GetAsync("/programisci?contract=1");
        Assert.Equal(HttpStatusCode.OK, filtered.Status);
        Assert.Contains("contract=1", web.Directory.Requests.Last(r => r.PathAndQuery.StartsWith("/api/v1/practitioners")).PathAndQuery);
        var current = filtered.All("nav[aria-label=Filtry] section.filters__group").Last().QuerySelectorAll("a[aria-current=true]");
        Assert.Equal(["Dostępni do kontraktu"], current.Select(a => a.TextContent));
        Assert.Equal("Filtry (1)", filtered.TextOf("details.filters > summary"));
        Assert.Equal("1", filtered.One("form[role=search] input[name=contract]")!.GetAttribute("value")); // a search keeps the filter
        Assert.Equal("noindex,follow", filtered.One("meta[name=robots]")!.GetAttribute("content")); // a filtered list is not indexed
    }

    [Theory]
    [InlineData("0")]
    [InlineData("yes")]
    [InlineData("")]
    public async Task Only_the_value_one_turns_the_filter_on(string value)
    {
        await using var web = ListHost(Sample.Card());
        var page = await web.NewBrowser().GetAsync("/programisci?contract=" + value);
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.DoesNotContain("contract", web.Directory.Requests.Last(r => r.PathAndQuery.StartsWith("/api/v1/practitioners")).PathAndQuery);
        Assert.Equal("index,follow", page.One("meta[name=robots]")!.GetAttribute("content"));
    }
}
