using System.Net;
using Bunit;
using Gabinety.Web.Api;
using Gabinety.Web.Components.Pages;
using Gabinety.Web.Hosting;
using Microsoft.AspNetCore.Http;

namespace Gabinety.Web.Tests.Rendering;

/// <summary>The home page (UX-011) with bUnit: the Directory answers from a fake handler, and the page renders the same code the host runs.</summary>
public class HomePageTests : BunitContext
{
    // The eight seeded problems (src/Gabinety.Directory/Persistence/Seeds/taxonomy.json), so every tile has its own sentence.
    TaxonomyDto _taxonomy = new("2026-11-v1",
    [
        new("audyt-kodu", "Audyt kodu"), new("migracja-legacy-dotnet", "Migracja legacy .NET"), new("integracja-ai", "Integracja AI"), new("automatyzacja-procesow", "Automatyzacja procesów"),
        new("architektura-systemu", "Architektura systemu"), new("code-review", "Code review"), new("wycena-projektu", "Wycena i plan projektu"), new("rozwiazanie-awarii", "Rozwiązanie awarii"),
    ], [new("dotnet", ".NET")]);

    SitemapDto _sitemap = new(
    [
        new("/programisci", 9, true), new("/programisci/audyt-kodu", 6, true), new("/programisci/migracja-legacy-dotnet", 1, false),
        new("/programisci/integracja-ai", 2, true), new("/programisci/automatyzacja-procesow", 5, true),
    ]);

    PageDto<PractitionerCardDto> _cards = Sample.Cards(5);
    bool _taxonomyDown, _sitemapDown, _practitionersDown;

    public HomePageTests()
    {
        Services.AddSingleton(new WebOptions());
        var http = new HttpClient(new FakeBackend { Respond = Answer }) { BaseAddress = new Uri("http://directory.test") };
        Services.AddSingleton(new DirectoryClient(http, new VisitorAddressAccessor(new HttpContextAccessor())));
    }

    HttpResponseMessage Answer(HttpRequestMessage r) => r.RequestUri!.AbsolutePath switch
    {
        "/api/v1/taxonomy" when _taxonomyDown => throw new HttpRequestException("down"),
        "/api/v1/sitemap" when _sitemapDown => throw new HttpRequestException("down"),
        "/api/v1/practitioners" when _practitionersDown => throw new HttpRequestException("down"),
        "/api/v1/taxonomy" => FakeBackend.Json(_taxonomy),
        "/api/v1/sitemap" => FakeBackend.Json(_sitemap),
        "/api/v1/practitioners" => FakeBackend.Json(_cards),
        _ => new HttpResponseMessage(HttpStatusCode.NotFound),
    };

    /// <summary>Renders the page and waits for its reads: the trust band is the last region and renders only after all of them.</summary>
    IRenderedComponent<Home> Open()
    {
        var cut = Render<Home>();
        cut.WaitForState(() => cut.FindAll("[data-testid=trust]").Count > 0);
        return cut;
    }

    static string Plain(string text) => text.Replace(' ', ' ');

    [Fact]
    public void Each_tile_has_its_own_sentence_and_the_practitioner_count_from_the_sitemap()
    {
        var cut = Open();
        var tiles = cut.FindAll("[data-testid=categories] > li");
        Assert.Equal(8, tiles.Count);
        Assert.All(tiles, t => Assert.True(t.ClassList.Contains("card") && t.ClassList.Contains("card--link"))); // the tile is a card whose link stretches over it

        var texts = tiles.Select(t => t.QuerySelector(".tile__text")!.TextContent).ToList();
        Assert.Equal(8, texts.Distinct().Count());
        Assert.Equal("Przegląd istniejącego kodu: jakość, bezpieczeństwo i plan poprawek.", texts[0]);

        Assert.Equal("6 programistów", tiles[0].QuerySelector(".tile__count")!.TextContent);      // audyt-kodu
        Assert.Equal("1 programista", tiles[1].QuerySelector(".tile__count")!.TextContent);      // migracja-legacy-dotnet: singular
        Assert.Equal("2 programiści", tiles[2].QuerySelector(".tile__count")!.TextContent);      // integracja-ai: the few form
        Assert.Equal("5 programistów", tiles[3].QuerySelector(".tile__count")!.TextContent);     // automatyzacja-procesow: the many form
        Assert.Null(tiles[4].QuerySelector(".tile__count"));                                    // architektura-systemu is not in the sitemap: no count, no guess
    }

    [Fact]
    public void A_tile_link_keeps_the_category_name_as_its_accessible_name()
    {
        var cut = Open();
        var links = cut.FindAll("[data-testid=categories] a.tile__link");
        Assert.Equal(["Audyt kodu", "Migracja legacy .NET"], links.Take(2).Select(a => a.TextContent));
        Assert.Equal("/programisci/migracja-legacy-dotnet", links[1].GetAttribute("href"));
    }

    [Fact]
    public void A_slug_the_page_does_not_know_gets_a_sentence_that_names_it_and_no_count_it_cannot_back()
    {
        _taxonomy = new("2026-12-v1", [new("nowa-kategoria", "Nowa kategoria")], []);
        _sitemap = new([]);
        var cut = Open();
        var tile = cut.Find("[data-testid=categories] > li");
        Assert.Equal("Konsultacja z programistą w obszarze: Nowa kategoria.", tile.QuerySelector(".tile__text")!.TextContent);
        Assert.Null(tile.QuerySelector(".tile__count"));
    }

    [Fact]
    public void The_featured_row_shows_three_practitioners_as_card_links_with_their_price()
    {
        var cut = Open();
        var region = cut.Find("[data-testid=featured]");
        Assert.Equal("home-featured", cut.Find("section[aria-labelledby=home-featured]").GetAttribute("aria-labelledby"));
        var cards = region.Children;
        Assert.Equal(3, cards.Length);                                       // the first page, three of it
        Assert.All(cards, c => Assert.Contains("card--link", c.ClassName));
        Assert.Equal(("Osoba Demo 1", "/gabinet/demo-person-1"), (cards[0].QuerySelector("h3 a")!.TextContent, cards[0].QuerySelector("h3 a")!.GetAttribute("href")));
        Assert.Equal("Audyty kodu .NET i konsultacje architektury", cards[0].QuerySelector(".featured__headline")!.TextContent);
        Assert.Equal("od 250,00 zł", Plain(cards[0].QuerySelector(".featured__price")!.TextContent));
    }

    [Fact]
    public void The_featured_region_fails_soft_and_the_rest_of_the_page_stays()
    {
        _practitionersDown = true;
        var cut = Open();
        Assert.Empty(cut.FindAll("[data-testid=featured]"));
        Assert.Empty(cut.FindAll("#home-featured"));
        Assert.Empty(cut.FindAll("[role=alert]"));                            // no error callout for an optional region
        Assert.Equal(8, cut.FindAll("[data-testid=categories] > li").Count);
        Assert.Single(cut.FindAll("h1"));
        Assert.NotEmpty(cut.FindAll("form[role=search]"));
    }

    [Fact]
    public void A_failing_sitemap_leaves_tiles_without_counts_and_the_trust_row_without_a_number()
    {
        _sitemapDown = true;
        var cut = Open();
        Assert.Equal(8, cut.FindAll("[data-testid=categories] > li").Count);
        Assert.Empty(cut.FindAll("[data-testid=categories] .tile__count"));
        var trust = cut.FindAll("[data-testid=trust] li").Select(li => li.TextContent).ToList();
        Assert.DoesNotContain(trust, t => t.Contains("zweryfikowan"));
        Assert.Equal(2, trust.Count);
    }

    [Fact]
    public void A_failing_taxonomy_is_an_error_state_of_the_categories_only()
    {
        _taxonomyDown = true;
        var cut = Open();
        Assert.Equal("alert", cut.Find("[data-testid=categories-error]").GetAttribute("role"));
        Assert.Empty(cut.FindAll("[data-testid=categories]"));
        Assert.Equal(4, cut.FindAll("details").Count); // the questions and the call to action still render
        Assert.Single(cut.FindAll("h1"));
    }

    [Theory]
    [InlineData(1, "1 zweryfikowany programista")]
    [InlineData(3, "3 zweryfikowani programiści")]
    [InlineData(12, "12 zweryfikowanych programistów")]
    [InlineData(22, "22 zweryfikowani programiści")]
    [InlineData(25, "25 zweryfikowanych programistów")]
    public void The_trust_row_states_the_verified_count_in_the_right_polish_form(int verified, string expected)
    {
        _sitemap = new([new("/programisci", verified, true)]);
        var cut = Open();
        Assert.Equal(expected, cut.Find("[data-testid=trust] li").TextContent);
    }

    [Fact]
    public void The_trust_row_keeps_the_existing_statements()
    {
        var trust = Open().Find("[data-testid=trust]").TextContent;
        Assert.Contains("Płatność z góry, programista dostaje pieniądze po konsultacji", trust);
        Assert.Contains("Ranking bez płatnych miejsc", trust);
    }

    [Fact]
    public void The_page_has_one_h1_the_example_queries_and_the_call_to_action_for_programmers()
    {
        var cut = Open();
        Assert.Single(cut.FindAll("h1"));
        Assert.Equal(
            ["/programisci?q=audyt%20kodu", "/programisci?q=migracja%20.NET", "/programisci?q=integracja%20AI"],
            cut.FindAll(".examples a.chip").Select(a => a.GetAttribute("href")));
        Assert.Equal(["audyt kodu", "migracja .NET", "integracja AI"], cut.FindAll(".examples a.chip").Select(a => a.TextContent));
        var cta = cut.Find("a.btn.btn--secondary");
        Assert.Equal("/rejestracja", cta.GetAttribute("href"));
    }

    [Fact]
    public void The_four_questions_are_native_details_and_the_answers_point_to_the_price_list_instead_of_stating_amounts()
    {
        var cut = Open();
        var details = cut.FindAll("details");
        Assert.Equal(["Kiedy płacę?", "Czy mogę odwołać konsultację?", "Ile płacę platformie?", "Co, jeśli konsultacja się nie odbyła?"],
            details.Select(d => d.QuerySelector("summary")!.TextContent));
        Assert.Contains("Płacisz z góry", details[0].TextContent);
        Assert.Contains("24 godziny", details[1].TextContent);
        Assert.Contains("pełny zwrot", details[1].TextContent);
        Assert.Contains("pierwszej zrealizowanej konsultacji", details[2].TextContent);
        Assert.Equal("/cennik", details[2].QuerySelector("a")!.GetAttribute("href"));
        Assert.Contains("48 godzin", details[3].TextContent);
        Assert.DoesNotContain("zł", string.Concat(details.Select(d => d.TextContent))); // no amount of CONTRACTS section 9 on the page
    }
}
