using Gabinety.Web.Api;
using Gabinety.Web.Components.Pages.Practitioners;
using Gabinety.Web.Components.Shared;

namespace Gabinety.Web.Tests.Models;

/// <summary>Plain C# model tests: no renderer (BLAZOR-WEB-APP section 10). They mirror web/src/lib/seo/listUrl.ts and metadata.ts.</summary>
public class ListStateTests
{
    [Fact] public void An_empty_request_is_the_plain_list() => Assert.Equal("/programisci", ListState.Parse(null, null, null, null, null, null).Href());

    [Theory]
    [InlineData("30", 30)] [InlineData("60", 60)] [InlineData("45", null)] [InlineData("abc", null)] [InlineData("-30", null)] [InlineData(null, null)]
    public void Duration_is_30_or_60_and_anything_else_is_dropped(string? raw, int? expected) => Assert.Equal(expected, ListState.Parse(null, null, raw, null, null, null).Duration);

    [Theory]
    [InlineData("15000", 15000L)] [InlineData("0", null)] [InlineData("012", null)] [InlineData("1234567890", null)] [InlineData("12.5", null)] [InlineData("x", null)]
    public void Max_price_is_a_positive_integer_of_at_most_nine_digits(string raw, long? expected) => Assert.Equal(expected, ListState.Parse(null, null, null, raw, null, null).MaxPriceMinor);

    [Fact]
    public void The_search_text_is_trimmed_and_limited_to_60_characters()
    {
        Assert.Equal("migracja", ListState.Parse(null, null, null, null, "  migracja ", null).Q);
        Assert.Null(ListState.Parse(null, null, null, null, new string('a', 61), null).Q);
        Assert.Null(ListState.Parse(null, null, null, null, "   ", null).Q);
        Assert.Null(ListState.Parse(null, null, null, null, null, new string('c', 513)).Cursor);
    }

    [Fact] public void A_tech_without_a_problem_is_ignored() => Assert.Null(ListState.Parse(null, "dotnet", null, null, null, null).Tech);

    [Fact]
    public void Links_keep_the_other_filters_and_return_to_the_first_page()
    {
        var s = ListState.Parse("audyt-kodu", "dotnet", "60", "30000", "kod", "CUR");
        Assert.Equal("/programisci/audyt-kodu/dotnet?duration=60&maxPriceMinor=30000&q=kod&cursor=CUR", s.Href());
        Assert.Equal("/programisci/audyt-kodu/dotnet?duration=60&maxPriceMinor=30000&q=kod", s.FirstPage().Href());
        Assert.Equal("/programisci/audyt-kodu/dotnet?maxPriceMinor=30000&q=kod", s.WithDuration(null).Href());
        Assert.Equal("/programisci/migracja?duration=60&maxPriceMinor=30000&q=kod", s.WithProblem("migracja").Href()); // a new problem drops the technology and the cursor
        Assert.Equal("/programisci/audyt-kodu/dotnet", s.Cleared().Href());
    }

    [Fact] public void The_search_text_is_escaped_in_a_link() => Assert.Equal("/programisci?q=a%26b%20c", new ListState(Q: "a&b c").Href());

    [Theory]
    [InlineData(null, null, null, null, false)] [InlineData("30", null, null, null, true)] [InlineData(null, "15000", null, null, true)] [InlineData(null, null, "x", null, true)] [InlineData(null, null, null, "c", true)]
    public void A_filtered_or_paginated_page_is_a_variant(string? d, string? p, string? q, string? c, bool variant) => Assert.Equal(variant, ListState.Parse("audyt-kodu", null, d, p, q, c).IsVariant);
}

public class ListViewTests
{
    static ListView View(ListState? s = null, int count = 6, bool indexable = true, string path = "/programisci") =>
        new(s ?? new ListState(), Sample.Taxonomy, Sample.Sitemap(count, indexable, path));

    [Fact]
    public void The_plain_list_has_the_all_title_and_the_count_of_the_sitemap()
    {
        var v = View();
        Assert.Equal("Programiści", v.Heading);
        Assert.Equal("Wyniki: 6", v.CountText(2, false));
        Assert.Equal("Programiści do konsultacji online | Gabinety Tech", v.MetaTitle);
    }

    [Fact]
    public void A_category_and_a_technology_name_themselves_from_the_taxonomy()
    {
        var c = View(new ListState("audyt-kodu"), path: "/programisci/audyt-kodu");
        Assert.Equal("Audyt kodu", c.Heading);
        Assert.Equal("Audyt kodu: konsultacje z programistą | Gabinety Tech", c.MetaTitle);
        var t = View(new ListState("audyt-kodu", "dotnet"), path: "/programisci/audyt-kodu/dotnet");
        Assert.Equal("Audyt kodu: .NET", t.Heading);
        Assert.Contains("Audyt kodu i .NET", t.MetaTitle);
    }

    [Fact] public void An_unknown_problem_or_technology_is_not_known() { Assert.False(View(new ListState("nie-ma")).IsKnown); Assert.False(View(new ListState("audyt-kodu", "nie-ma")).IsKnown); Assert.True(View(new ListState("audyt-kodu", "dotnet")).IsKnown); }

    [Theory]
    [InlineData(6, true, false, "index,follow")]
    [InlineData(2, false, false, "noindex,follow")] // fewer than 3 approved practitioners: the API says indexable=false
    [InlineData(6, true, true, "noindex,follow")]   // a filtered variant is never indexed, whatever the category says
    public void Robots_follow_the_indexable_flag_and_variants(int count, bool indexable, bool variant, string robots) =>
        Assert.Equal(robots, View(new ListState(Duration: variant ? 30 : null), count, indexable).Robots);

    [Fact] public void A_variant_points_at_the_plain_list_as_its_canonical() => Assert.Equal("/programisci/audyt-kodu", View(new ListState("audyt-kodu", Duration: 30, Cursor: "x"), path: "/programisci/audyt-kodu").CanonicalPath);

    [Fact]
    public void Filters_mark_the_current_choice_and_offer_technologies_only_inside_a_problem()
    {
        var plain = View().Filters();
        Assert.Equal(["Problem", "Czas trwania", "Cena do"], plain.Select(g => g.Title));
        Assert.True(plain[0].Items[0].Current); // "Wszystkie"

        var inside = View(new ListState("audyt-kodu", Duration: 60)).Filters();
        Assert.Equal(["Problem", "Technologia", "Czas trwania", "Cena do"], inside.Select(g => g.Title));
        Assert.Contains(inside[0].Items, i => i is { Label: "Audyt kodu", Current: true });
        Assert.Contains(inside[2].Items, i => i is { Label: "60 min", Current: true, Href: "/programisci/audyt-kodu?duration=60" });
        Assert.Contains(inside[3].Items, i => i.Label == "do 150,00 zł" && i.Href == "/programisci/audyt-kodu?duration=60&maxPriceMinor=15000");
    }
}

public class MoneyTests
{
    [Theory]
    [InlineData(0L, "0,00 zł")] [InlineData(5L, "0,05 zł")] [InlineData(25000L, "250,00 zł")] [InlineData(150000L, "1 500,00 zł")] [InlineData(123456789L, "1 234 567,89 zł")]
    public void Grosz_are_formatted_with_integer_arithmetic(long grosz, string expected) => Assert.Equal(expected, Money.Pln(grosz));

    [Fact] public void A_negative_amount_is_a_programming_error() => Assert.Throws<ArgumentOutOfRangeException>(() => Money.Pln(-1));
}
