using Gabinety.Web.Components.Pages.Practitioners;

namespace Gabinety.Web.Tests.Models;

/// <summary>T-11.05: the order of the list (<c>sortuj</c>). Plain C# model tests, no renderer.</summary>
public class ListSortTests
{
    [Theory]
    [InlineData("cena-rosnaco", "price_asc")] [InlineData("cena-malejaco", "price_desc")] [InlineData("termin", "next_slot")]
    public void Each_sort_key_maps_to_its_directory_value(string key, string wire)
    {
        var s = ListState.Parse(null, null, null, null, null, null, key);
        Assert.Equal(key, s.Sort);
        Assert.Equal(wire, s.ApiSort);
    }

    [Theory]
    [InlineData("trafnosc")] [InlineData("cena")] [InlineData("")] [InlineData(null)] [InlineData("PRICE_ASC")]
    public void The_default_and_anything_unknown_is_no_sort_and_no_wire_value(string? raw)
    {
        var s = ListState.Parse(null, null, null, null, null, null, raw);
        Assert.Null(s.Sort);
        Assert.Null(s.ApiSort);
        Assert.False(s.IsVariant);
    }

    [Fact]
    public void The_default_is_never_written_to_a_link_and_a_chosen_sort_is()
    {
        Assert.Equal("/programisci", ListState.Parse(null, null, null, null, null, null, "trafnosc").Href());
        Assert.Equal("/programisci?sortuj=cena-rosnaco", ListState.Parse(null, null, null, null, null, null, "cena-rosnaco").Href());
    }

    [Fact]
    public void Every_filter_link_keeps_the_order_and_returns_to_the_first_page()
    {
        var s = ListState.Parse("audyt-kodu", "dotnet", "60", "30000", "kod", "CUR", "termin");
        Assert.Equal("/programisci/audyt-kodu/dotnet?duration=60&maxPriceMinor=30000&q=kod&sortuj=termin&cursor=CUR", s.Href());
        Assert.Equal("/programisci/audyt-kodu/dotnet?duration=60&maxPriceMinor=30000&q=kod&sortuj=termin", s.FirstPage().Href());
        Assert.Equal("/programisci/audyt-kodu/dotnet?maxPriceMinor=30000&q=kod&sortuj=termin", s.WithDuration(null).Href());
        Assert.Equal("/programisci/migracja?duration=60&maxPriceMinor=30000&q=kod&sortuj=termin", s.WithProblem("migracja").Href());
        Assert.Equal("/programisci/audyt-kodu?duration=60&maxPriceMinor=30000&q=kod&sortuj=termin", s.WithTech(null).Href());
        Assert.Equal("/programisci/audyt-kodu/dotnet?sortuj=termin", s.Cleared().Href()); // clearing the filters keeps the order
    }

    [Fact]
    public void Changing_the_order_starts_again_from_the_first_page_and_the_default_clears_it()
    {
        var s = ListState.Parse("audyt-kodu", null, null, null, null, "CUR", "cena-rosnaco");
        Assert.Equal("/programisci/audyt-kodu?sortuj=termin", s.WithSort("termin").Href());
        Assert.Equal("/programisci/audyt-kodu", s.WithSort("trafnosc").Href());
        Assert.Equal("/programisci/audyt-kodu", s.WithSort("nie-ma").Href()); // an unknown key is the default, never an error
    }

    [Fact]
    public void A_sorted_page_is_a_variant_so_it_is_not_indexed_and_its_canonical_is_the_plain_list()
    {
        var sorted = ListState.Parse("audyt-kodu", null, null, null, null, null, "cena-malejaco");
        Assert.True(sorted.IsVariant);
        Assert.Equal("/programisci/audyt-kodu", sorted.Path);
    }

    [Fact]
    public void The_sort_text_is_not_a_filter_and_does_not_count_as_one()
    {
        var view = new ListView(ListState.Parse(null, null, null, null, null, null, "termin"), Sample.Taxonomy, Sample.Sitemap(6));
        Assert.Equal(0, view.ActiveFilters);
        Assert.Empty(view.ActiveChips());
    }

    [Fact]
    public void The_sort_control_offers_four_orders_and_marks_only_the_current_one()
    {
        var plain = new ListView(ListState.Parse(null, null, null, null, null, null), Sample.Taxonomy, Sample.Sitemap(6)).SortOptions();
        Assert.Equal(["trafnosc", "cena-rosnaco", "cena-malejaco", "termin"], plain.Select(o => o.Value));
        Assert.Equal(["Trafność", "Cena rosnąco", "Cena malejąco", "Najbliższy termin"], plain.Select(o => o.Label));
        Assert.Equal([true, false, false, false], plain.Select(o => o.Selected));

        var cheapest = new ListView(ListState.Parse(null, null, null, null, null, null, "cena-rosnaco"), Sample.Taxonomy, Sample.Sitemap(6)).SortOptions();
        Assert.Equal([false, true, false, false], cheapest.Select(o => o.Selected));
    }
}
