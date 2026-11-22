using System.Text.Json;
using Bunit;
using Gabinety.Web.Components.Pages.Gabinet;
using Gabinety.Web.Components.Pages.Static;

namespace Gabinety.Web.Tests.Public;

public class LegalMarkdownTests : BunitContext
{
    const string Front = "---\nversion: 1.0\ndate: 2026-11-17\ndraft: true\n---\n";

    [Fact]
    public void Front_matter_and_the_markdown_subset_become_blocks()
    {
        var doc = LegalMarkdown.Parse(Front + "# Tytuł\n\nPierwszy akapit\ndrugi wiersz.\n\n## 1. Sekcja\n\n- a\n- b\n\n1. raz\n2) dwa\n\n### Pod\ntekst");
        Assert.Equal(("1.0", "2026-11-17", true), (doc.Version, doc.Date, doc.Draft));
        Assert.Equal([BlockKind.Heading, BlockKind.Paragraph, BlockKind.Heading, BlockKind.BulletList, BlockKind.NumberedList, BlockKind.Heading, BlockKind.Paragraph], doc.Blocks.Select(b => b.Kind));
        Assert.Equal([2, 0, 3, 0, 0, 4, 0], doc.Blocks.Select(b => b.Level)); // the file's # is the page's h2: the page owns the only h1
        Assert.Equal("Pierwszy akapit drugi wiersz.", doc.Blocks[1].Text);
        Assert.Equal(["a", "b"], doc.Blocks[3].Items);
        Assert.Equal(["raz", "dwa"], doc.Blocks[4].Items);
    }

    [Fact]
    public void A_file_without_version_or_date_is_refused()
    {
        Assert.Throws<FormatException>(() => LegalMarkdown.Parse("# bez front matter"));
        Assert.Throws<FormatException>(() => LegalMarkdown.Parse("---\ndate: 2026-11-17\n---\ntekst"));
        Assert.Throws<FormatException>(() => LegalMarkdown.Parse("---\nversion: 1\ndate: 17.11.2026\n---\ntekst"));
    }

    [Fact]
    public void Heading_ids_fold_Polish_letters_and_are_unique()
    {
        var doc = LegalMarkdown.Parse(Front + "## Zażółć gęślą\n## Zażółć gęślą\n## ???");
        Assert.Equal(["s-zazolc-gesla", "s-zazolc-gesla-2", "sekcja"], doc.Blocks.Select(b => b.Id));
    }

    [Fact]
    public void HTML_in_the_file_is_text_never_markup()
    {
        // XSS: nothing of the file is passed through as HTML; Razor encodes every block.
        var doc = LegalMarkdown.Parse(Front + "## <script>alert(1)</script>\n\n<img src=x onerror=alert(2)> **tekst**\n\n- <b>pogrubione</b>\n- [link](javascript:alert(3))");
        var cut = Render<LegalPage>(p => p.Add(c => c.Title, "Regulamin").Add(c => c.Doc, doc));
        Assert.Empty(cut.FindAll("script"));
        Assert.Empty(cut.FindAll("img"));
        Assert.Empty(cut.FindAll("b"));
        Assert.Empty(cut.FindAll("a[href^=javascript]"));
        Assert.Contains("<script>alert(1)</script>", cut.Find("h3").TextContent); // shown as text
        Assert.Contains("&lt;img src=x onerror=alert(2)&gt;", cut.Markup);
        Assert.Contains("[link](javascript:alert(3))", cut.Markup.Replace("&amp;", "&")); // links are not a feature of the subset: plain text
    }

    [Fact]
    public void The_page_has_one_h1_a_contents_list_linking_the_headings_and_the_draft_notice()
    {
        var doc = LegalMarkdown.Parse(Front + "# Regulamin\n\n## 1. Ogólne\n\ntekst\n\n## 2. Płatność\n\ntekst");
        var cut = Render<LegalPage>(p => p.Add(c => c.Title, "Regulamin").Add(c => c.Doc, doc));
        Assert.Single(cut.FindAll("h1"));
        Assert.Equal(["#s-1-ogolne", "#s-2-platnosc"], cut.FindAll("nav.contents a").Select(a => a.GetAttribute("href")));
        Assert.NotNull(cut.Find("h3#s-1-ogolne"));
        Assert.Contains("Wersja robocza", cut.Find("[data-testid=draft-notice]").TextContent);
        Assert.Equal("17.11.2026", cut.Find("p.cluster span:last-child").TextContent.Replace("Data: ", ""));
    }

    [Theory]
    [InlineData("regulamin")] [InlineData("polityka-prywatnosci")]
    public void The_embedded_text_is_the_owners_file_and_the_resource_is_the_file(string name)
    {
        var mine = File.ReadAllText(Path.Combine(Repo.Web, "Content", "Legal", name + ".md")); // the one copy since the cutover (the Next.js copy went with web/)
        var doc = LegalMarkdown.Load(name);
        Assert.NotEmpty(doc.Blocks);
        Assert.Equal(LegalMarkdown.Parse(mine).Version, doc.Version); // the embedded resource is the file
    }
}

public class ProfileJsonLdTests
{
    static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void The_json_parses_and_names_the_practitioner_the_offers_and_the_links()
    {
        var ld = Parse(ProfileJsonLd.Build(PublicSupport.Profile(), "http://localhost/gabinet/demo-anna-demowska"));
        Assert.Equal(("https://schema.org", "ProfessionalService", "Anna Ąćęł Demowska"), (ld.GetProperty("@context").GetString(), ld.GetProperty("@type").GetString(), ld.GetProperty("name").GetString()));
        Assert.Equal("http://localhost/gabinet/demo-anna-demowska", ld.GetProperty("url").GetString());
        Assert.Equal(["https://demo.example/portfolio", "https://github.com/demo-anna"], ld.GetProperty("sameAs").EnumerateArray().Select(e => e.GetString()));
        var offer = ld.GetProperty("makesOffer")[0];
        Assert.Equal(("Offer", "Audyt kodu .NET", "180.00", "PLN"), (offer.GetProperty("@type").GetString(), offer.GetProperty("name").GetString(), offer.GetProperty("price").GetString(), offer.GetProperty("priceCurrency").GetString()));
    }

    [Fact]
    public void The_display_name_is_readable_in_the_text_for_the_journey_that_looks_for_it() =>
        Assert.Contains("Anna Ąćęł Demowska", ProfileJsonLd.Build(PublicSupport.Profile(), "http://localhost/x"));

    [Fact]
    public void Text_cannot_close_the_script_element()
    {
        var json = ProfileJsonLd.Build(PublicSupport.Profile(headline: "</script><script>alert(1)</script><!--"), "http://localhost/x");
        Assert.DoesNotContain("<", json);
        Assert.Equal("</script><script>alert(1)</script><!--", Parse(json).GetProperty("description").GetString()); // the data is intact once parsed
    }

    [Theory]
    [InlineData(2, false)] [InlineData(3, true)] [InlineData(25, true)]
    public void The_aggregate_rating_needs_at_least_three_reviews(int count, bool present)
    {
        var ld = Parse(ProfileJsonLd.Build(PublicSupport.Profile(isNew: false, rating: new Gabinety.Web.Api.RatingDto(4.5, count)), "http://localhost/x"));
        Assert.Equal(present, ld.TryGetProperty("aggregateRating", out var agg));
        if (present) Assert.Equal((4.5, count, 5, 1), (agg.GetProperty("ratingValue").GetDouble(), agg.GetProperty("reviewCount").GetInt32(), agg.GetProperty("bestRating").GetInt32(), agg.GetProperty("worstRating").GetInt32()));
    }

    [Theory]
    [InlineData("https://demo.example/a", "https://demo.example/a")] [InlineData("http://demo.example/a", null)] [InlineData("javascript:alert(1)", null)] [InlineData("//demo.example", null)] [InlineData("nonsense", null)] [InlineData(null, null)]
    public void A_link_is_shown_and_listed_only_when_it_is_https(string? url, string? expected) => Assert.Equal(expected, ProfileJsonLd.SafeHttps(url));

    [Fact]
    public void An_unsafe_portfolio_is_not_in_the_structured_data_and_a_missing_one_leaves_no_sameAs()
    {
        var ld = Parse(ProfileJsonLd.Build(PublicSupport.Profile(portfolio: "javascript:alert(1)", github: null), "http://localhost/x"));
        Assert.False(ld.TryGetProperty("sameAs", out _));
    }
}

public class RankingAndFeeTests
{
    [Fact]
    public void The_worked_example_reproduces_the_published_vector()
    {
        // CONTRACTS 9.3, second vector, by hand: B = (10*4.8+5*3.8)/15/5, C = 23/25, A = 5/10, P = 0.8: 40.2 + 23 + 7.5 + 12 = 82.7.
        var (parts, score) = RankingModel.Worked(PublicSupport.Rules);
        Assert.Equal(82.70, score);
        Assert.Equal([40.2, 23.0, 7.5, 12.0], parts.Select(p => Math.Round(p.Points, 2)));
        Assert.Equal(["rating", "reliability", "availability", "completeness"], parts.Select(p => p.Key));
    }

    [Fact]
    public void The_example_follows_the_rules_it_is_given_and_not_numbers_of_this_project()
    {
        var other = PublicSupport.Rules with { Weights = new(0.25, 0.25, 0.25, 0.25), Constants = new(5, 3.8, 3, 5) };
        var (_, score) = RankingModel.Worked(other);
        Assert.Equal(Math.Round(25 * (0.89333333 + 0.92 + 1 + 0.8), 2), score, 2);
    }

    [Theory]
    [InlineData(null, 1500)] [InlineData("", 1500)] [InlineData("abc", 1500)] [InlineData("-5", 1500)] [InlineData("10001", 1500)] [InlineData("1.5", 1500)] [InlineData("0", 0)] [InlineData(" 1000 ", 1000)] [InlineData("10000", 10000)]
    public void The_fee_rate_is_an_integer_in_0_to_10000_else_the_default(string? raw, int expected) => Assert.Equal(expected, Fee.BpsFrom(raw));

    [Theory]
    [InlineData(25000, 1500, 3750)] [InlineData(25000, 1000, 2500)] [InlineData(10001, 1500, 1500)] [InlineData(10003, 1500, 1500)] [InlineData(333, 1500, 50)] [InlineData(0, 1500, 0)]
    public void The_fee_rounds_half_up_in_integer_grosz(long price, int bps, long expected) => Assert.Equal(expected, Fee.For(price, bps)); // CONTRACTS 9.1
}
