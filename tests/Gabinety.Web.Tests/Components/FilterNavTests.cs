using Bunit;
using Gabinety.Web.Components.Pages.Practitioners;

namespace Gabinety.Web.Tests.Rendering;

/// <summary>UX-013 (V-3): the markup and the open-state rule of the list filters. The layout (52 rem, equal columns) is CSS, so the scoped sheet is read from disk.</summary>
public class FilterNavTests : BunitContext
{
    /// <summary>Renders the panel the way <see cref="ListView"/> feeds it on the page.</summary>
    IRenderedComponent<FilterNav> Nav(ListState state)
    {
        var view = new ListView(state, Sample.Taxonomy, Sample.Sitemap(5, true, "/programisci/audyt-kodu"));
        return Render<FilterNav>(p => p.Add(f => f.State, state).Add(f => f.Groups, view.Filters()).Add(f => f.ActiveCount, view.ActiveFilters));
    }

    [Fact]
    public void Duration_and_price_are_joined_segmented_lists_with_one_link_chip_per_item()
    {
        var cut = Nav(new ListState("audyt-kodu"));
        var segmented = cut.FindAll("nav[aria-label=Filtry] ul.segmented");
        Assert.Equal(2, segmented.Count);
        Assert.Equal(["Wszystkie", "30 min", "60 min"], segmented[0].QuerySelectorAll("a").Select(a => a.TextContent));
        Assert.Equal(["Dowolna", "do 150,00 zł", "do 300,00 zł", "do 500,00 zł"], segmented[1].QuerySelectorAll("a").Select(a => a.TextContent.Replace('\u00A0', ' '))); // Money puts a no-break space before zł
        foreach (var ul in segmented)
        {
            foreach (var li in ul.Children)
            {
                Assert.Equal("li", li.LocalName);
                Assert.Single(li.Children);
                Assert.Equal("a", li.Children[0].LocalName);
                Assert.True(li.Children[0].ClassList.Contains("chip"));
            }
        }
    }

    [Fact]
    public void The_problem_group_is_a_link_list_not_a_segmented_row()
    {
        var cut = Nav(new ListState());
        var problems = cut.FindAll("nav[aria-label=Filtry] section")[0].QuerySelector("ul")!;
        Assert.Equal("link-list", problems.ClassName);
        Assert.Equal(["Wszystkie", "Audyt kodu", "Migracja"], problems.QuerySelectorAll("a").Select(a => a.TextContent));
    }

    [Fact]
    public void Each_group_marks_its_active_choice_with_aria_current_and_nothing_else()
    {
        var cut = Nav(new ListState("audyt-kodu", Duration: 30));
        // problem: Audyt kodu; technology: Wszystkie (none chosen); duration: 30 min; price: Dowolna (none chosen)
        Assert.Equal(["Audyt kodu", "Wszystkie", "30 min", "Dowolna"], cut.FindAll("a[aria-current=true]").Select(a => a.TextContent));
        Assert.All(cut.FindAll("a[aria-current=true]"), a => Assert.True(a.ClassList.Contains("chip")));
    }

    [Fact]
    public void The_disclosure_is_closed_with_no_active_filter_and_open_when_one_is_active()
    {
        var closed = Nav(new ListState());
        Assert.False(closed.Find("details.filters").HasAttribute("open"));
        Assert.Equal("Filtry", closed.Find("details.filters > summary").TextContent);

        var open = Nav(new ListState("audyt-kodu", Duration: 60));
        Assert.True(open.Find("details.filters").HasAttribute("open"));
        Assert.Equal("Filtry (2)", open.Find("details.filters > summary").TextContent);
    }

    [Fact]
    public void The_search_form_heads_the_panel_and_keeps_the_other_filters_as_hidden_fields()
    {
        var cut = Nav(new ListState("audyt-kodu", MaxPriceMinor: 15000, Q: "kod"));
        Assert.Equal("form", cut.Find("nav[aria-label=Filtry]").Children[0].LocalName);
        var form = cut.Find("form[role=search]");
        Assert.Equal(("get", "/programisci/audyt-kodu"), (form.GetAttribute("method"), form.GetAttribute("action")));
        Assert.Equal("15000", form.QuerySelector("input[type=hidden][name=maxPriceMinor]")!.GetAttribute("value"));
        Assert.Null(form.QuerySelector("input[type=hidden][name=duration]"));
        var search = form.QuerySelector("input[name=q]")!;
        Assert.Equal("search", search.GetAttribute("type"));
        Assert.Equal("Szukaj po imieniu lub nagłówku", search.GetAttribute("aria-label"));
        Assert.Equal("Szukaj", form.QuerySelector("button[type=submit]")!.TextContent);
    }

    [Fact]
    public void The_scoped_sheet_shows_the_panel_from_52_rem_keeps_the_summary_inert_there_and_has_no_inline_flow_left()
    {
        var css = File.ReadAllText(Path.Combine(Repo.Web, "Components", "Pages", "Practitioners", "FilterNav.razor.css"));
        Assert.Contains("@media (min-width: 52rem)", css);
        Assert.Contains(".filters:not([open]) > :not(summary) { display: block; }", css); // block, not grid: a grid sized the nav by its widest chip and the panel slid under the results
        Assert.Contains(".filters > summary { pointer-events: none; }", css);
        Assert.Contains(".filters:not([open])::details-content { content-visibility: visible; }", css); // the slot a closed disclosure hides its content in
        Assert.DoesNotContain("inline-flex", css); // the joined rows are the shared equal-column grid of app.css, not an inline flow
        Assert.DoesNotContain("border-radius: 0", css); // the per-position corners are the shared sheet's
    }
}
