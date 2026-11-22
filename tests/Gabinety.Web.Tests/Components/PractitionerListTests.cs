using Bunit;
using Gabinety.Web.Api;
using Gabinety.Web.Components.Pages.Practitioners;
using Gabinety.Web.Components.Shared;

namespace Gabinety.Web.Tests.Rendering;

/// <summary>UX-012: the avatar, the practitioner card (whole-card link, Nowy, one verification badge, categories, price) and the list helpers (plural count, removable filter chips).</summary>
public class PractitionerListTests : BunitContext
{
    static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string>
    {
        ["audyt-kodu"] = "Audyt kodu", ["migracja"] = "Migracja", ["ai"] = "Integracja AI", ["testy"] = "Testy",
    };

    static PractitionerCardDto Card(string name = "Anna Demowska", IReadOnlyList<string>? problems = null, RatingDto? rating = null, bool isNew = false,
        IReadOnlyList<string>? badges = null, long price = 12000, IReadOnlyList<int>? durations = null) =>
        new(Guid.NewGuid(), "demo-anna-demowska", name, "Audyty kodu .NET", null, badges ?? [], rating, isNew, new MoneyDto(price, "PLN"), problems ?? [], durations ?? [30, 60]);

    [Theory]
    [InlineData("Anna Demowska", "AD")] [InlineData("Jan", "J")] [InlineData("  osoba   demo 1 ", "OD")] [InlineData("Łukasz Żółw", "ŁŻ")]
    [InlineData("jan kowalski", "JK")] [InlineData("Anna Maria Demowska", "AM")] [InlineData("", "?")]
    public void Initials_are_the_first_letters_of_the_first_two_words_in_uppercase(string name, string expected) => Assert.Equal(expected, Avatar.Letters(name));

    [Fact]
    public void A_tone_is_stable_for_a_name_and_all_five_tones_are_used()
    {
        var names = Enumerable.Range(1, 200).Select(i => $"Osoba {i}").ToList();
        Assert.All(names, n => Assert.Equal(Avatar.Tone(n), Avatar.Tone(n)));
        Assert.Equal(["accent", "danger", "neutral", "success", "warning"], names.Select(Avatar.Tone).Distinct().Order().ToArray());
        Assert.Equal("success", Avatar.Tone("")); // pins the hash: FNV-1a of the empty string is the offset basis, 2166136261, which is 1 mod 5
    }

    [Fact]
    public void The_avatar_is_decorative_and_carries_its_initials_size_and_tone_classes()
    {
        var cut = Render<Avatar>(p => p.Add(a => a.Name, "Anna Demowska").Add(a => a.Size, Avatar.AvatarSize.Large));
        var span = cut.Find("span");
        Assert.Equal("true", span.GetAttribute("aria-hidden")); // the name is the link text beside it
        Assert.Equal("AD", span.TextContent);
        Assert.Contains("avatar--large", span.ClassName);
        Assert.Contains($"avatar--{Avatar.Tone("Anna Demowska")}", span.ClassName);
    }

    [Fact]
    public void The_whole_card_is_the_link_and_its_only_link_is_named_by_the_display_name()
    {
        var cut = Render<PractitionerCard>(p => p.Add(c => c.Practitioner, Card()));
        Assert.NotNull(cut.Find("article.card.card--link"));
        var links = cut.FindAll("a");
        Assert.Single(links); // one link: the stretched link of the card, with no second target inside it
        Assert.Equal("Anna Demowska", links[0].TextContent);
        Assert.Equal("/gabinet/demo-anna-demowska", links[0].GetAttribute("href"));
    }

    [Theory]
    [InlineData(true, 5.0, 2)] // new, with a rating that would otherwise show
    [InlineData(false, null, 0)] // not new but without any rating yet
    public void A_practitioner_without_a_rating_is_marked_with_the_exact_Nowy_badge(bool isNew, double? average, int count)
    {
        var rating = average is { } a ? new RatingDto(a, count) : null;
        var cut = Render<PractitionerCard>(p => p.Add(c => c.Practitioner, Card(rating: rating, isNew: isNew)));
        Assert.Equal(["Nowy"], cut.FindAll(".badge").Select(b => b.TextContent));
        Assert.Empty(cut.FindAll(".practitioner-card__rating"));
    }

    [Fact]
    public void A_verified_practitioner_has_one_success_badge_and_no_other_check()
    {
        var cut = Render<PractitionerCard>(p => p.Add(c => c.Practitioner, Card(badges: ["email", "github", "nip", "approved"], rating: new RatingDto(4.8, 25))));
        var badges = cut.FindAll(".badge");
        Assert.Equal(["Zweryfikowany"], badges.Select(b => b.TextContent));
        Assert.Contains("badge--success", badges[0].ClassName);
        Assert.Equal("★ 4,8 (25)", cut.Find(".practitioner-card__rating").TextContent);
    }

    [Fact]
    public void Categories_are_the_names_of_the_first_three_known_problems()
    {
        var card = Card(problems: ["migracja", "nieznany", "audyt-kodu", "ai", "testy"]);
        var cut = Render<PractitionerCard>(p => p.Add(c => c.Practitioner, card).Add(c => c.CategoryNames, Names));
        Assert.Equal(["Migracja", "Audyt kodu", "Integracja AI"], cut.FindAll(".practitioner-card__categories span").Select(s => s.TextContent));
    }

    [Fact]
    public void Without_category_names_no_category_is_shown()
    {
        var cut = Render<PractitionerCard>(p => p.Add(c => c.Practitioner, Card(problems: ["migracja"])));
        Assert.Empty(cut.FindAll(".practitioner-card__categories"));
    }

    [Theory]
    [InlineData(12000, "od 120 zł")] // a whole amount: no grosz
    [InlineData(12050, "od 120,50 zł")] // grosz are shown when there are some
    [InlineData(120000, "od 1 200 zł")] // thousands are grouped
    public void The_price_is_the_compact_from_price_in_the_large_size(long price, string expected)
    {
        var cut = Render<PractitionerCard>(p => p.Add(c => c.Practitioner, Card(price: price)));
        Assert.Equal(expected, cut.Find(".practitioner-card__from").TextContent);
        Assert.Equal("30 min, 60 min", cut.Find(".practitioner-card__durations").TextContent);
    }

    [Theory]
    [InlineData(0, "0 programistów")] [InlineData(1, "1 programista")] [InlineData(2, "2 programistów")] [InlineData(4, "4 programistów")]
    [InlineData(5, "5 programistów")] [InlineData(12, "12 programistów")] [InlineData(21, "21 programistów")] [InlineData(22, "22 programistów")]
    [InlineData(24, "24 programistów")] [InlineData(25, "25 programistów")] [InlineData(101, "101 programistów")]
    public void The_count_is_singular_only_for_one(int count, string expected) => Assert.Equal(expected, ListFormat.PractitionerCount(count));

    [Theory]
    [InlineData(12000, "120 zł")] [InlineData(12050, "120,50 zł")] [InlineData(0, "0 zł")] [InlineData(30000, "300 zł")]
    public void The_list_price_drops_the_grosz_only_when_whole(long grosz, string expected) => Assert.Equal(expected, ListFormat.Price(grosz));

    [Fact]
    public void The_result_count_is_the_category_total_when_the_page_is_unfiltered_and_the_page_itself_when_it_is_the_whole_result()
    {
        var plain = new ListView(ListState.Parse(null, null, null, null, null, null), Sample.Taxonomy, Sample.Sitemap(6));
        Assert.Equal(6, plain.ResultCount(2, false));
        Assert.Equal(0, plain.ResultCount(0, false)); // an empty first page is zero, not the sitemap's total
        Assert.Equal(6, plain.ResultCount(2, true)); // an unfiltered page with a next page still states the category total
        var filtered = new ListView(ListState.Parse(null, null, "30", null, null, null), Sample.Taxonomy, Sample.Sitemap(6));
        Assert.Equal(2, filtered.ResultCount(2, false)); // a filtered whole result: its own items
        Assert.Null(filtered.ResultCount(2, true)); // a filtered page with more pages has no total
        var later = new ListView(ListState.Parse(null, null, "30", null, null, "C"), Sample.Taxonomy, Sample.Sitemap(6));
        Assert.Null(later.ResultCount(2, false)); // a later page of a variant has no total either
    }

    [Fact]
    public void The_active_filters_are_removable_chips_that_each_drop_only_their_own_filter_and_the_category_is_not_one()
    {
        var view = new ListView(ListState.Parse("audyt-kodu", "dotnet", "30", "30000", "kod", null), Sample.Taxonomy, Sample.Sitemap(6));
        var chips = view.ActiveChips();
        Assert.Equal(["30 min", "do 300 zł", "Szukaj: „kod”"], chips.Select(c => c.Label));
        Assert.Equal("/programisci/audyt-kodu/dotnet?maxPriceMinor=30000&q=kod", chips[0].Href);
        Assert.Equal("/programisci/audyt-kodu/dotnet?duration=30&q=kod", chips[1].Href);
        Assert.Equal("/programisci/audyt-kodu/dotnet?duration=30&maxPriceMinor=30000", chips[2].Href);
    }

    [Fact]
    public void A_plain_list_has_no_chips()
    {
        var view = new ListView(ListState.Parse(null, null, null, null, null, null), Sample.Taxonomy, Sample.Sitemap(6));
        Assert.Empty(view.ActiveChips());
    }

    [Fact]
    public void The_problem_names_come_from_the_taxonomy_by_slug()
    {
        var view = new ListView(ListState.Parse(null, null, null, null, null, null), Sample.Taxonomy, Sample.Sitemap(6));
        Assert.Equal("Audyt kodu", view.ProblemNames["audyt-kodu"]);
        Assert.Equal("Migracja", view.ProblemNames["migracja"]);
    }
}
