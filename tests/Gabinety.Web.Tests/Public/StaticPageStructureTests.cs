using System.Net;
using Bunit;
using Gabinety.Web.Api;
using Gabinety.Web.Components.Pages.Static;
using Gabinety.Web.Components.Shared;

namespace Gabinety.Web.Tests.Public;

/// <summary>UX-017 (V-9 to V-12): the structure of the static pages, the 404 and the error page, as a visitor with no JavaScript gets them.</summary>
public class StaticPageStructureTests
{
    [Fact]
    public async Task How_it_works_has_numbered_step_cards_for_each_role_and_a_closing_call_to_action()
    {
        await using var web = PublicSupport.Host();
        var page = await web.NewBrowser().GetAsync("/jak-to-dziala");
        Assert.Single(page.All("h1"));
        Assert.Equal(2, page.All("ol.steps").Count); // client and practitioner: numbered, not run-on lines
        Assert.Equal(8, page.All("ol.steps > li.step").Count); // 5 client steps and 3 practitioner steps
        Assert.All(page.All("ol.steps > li.step"), li => Assert.False(string.IsNullOrWhiteSpace(li.TextContent)));
        Assert.Equal(3, page.All("section.card").Count); // one card per section: client, payments, practitioner
        var cta = page.One("section[aria-labelledby=how-cta] a.btn.btn--primary")!;
        Assert.Equal("/programisci", cta.GetAttribute("href"));
        Assert.Equal("how-cta", page.All(".prose > section").Last().GetAttribute("aria-labelledby")); // the call to action closes the page
    }

    [Fact]
    public async Task The_ranking_tables_are_stacked_at_phone_width_with_a_label_on_every_value_cell_inside_the_focusable_region()
    {
        await using var web = PublicSupport.Host();
        var page = await web.NewBrowser().GetAsync("/zasady-rankingu");
        Assert.Equal(3, page.All("table.table--cards").Count);
        Assert.Empty(page.All(".table-wrap td:not([data-label])")); // no value cell can stand without its column name
        Assert.All(page.All("table.table--cards td[data-label]"), td => Assert.False(string.IsNullOrWhiteSpace(td.GetAttribute("data-label"))));
        Assert.Equal(3, page.All(".table-wrap[role=region][tabindex='0'][aria-label]").Count); // still the keyboard-reachable labelled region (ScrollTable)
    }

    [Fact]
    public async Task The_pricing_page_links_to_the_list_and_its_second_example_uses_the_same_rate()
    {
        await using var web = PublicSupport.Host();
        var page = await web.NewBrowser().GetAsync("/cennik");
        Assert.Single(page.All("h1"));
        Assert.Equal(2, page.All("table.table--cards").Count);
        Assert.Equal("Podział ceny konsultacji 120,00 zł", page.TextOf("section[aria-labelledby=pricing-example-2] caption").Replace(' ', ' '));
        Assert.Equal("/programisci", page.One(".prose a[href='/programisci']")!.GetAttribute("href"));
    }

    [Fact]
    public void The_legal_contents_list_is_the_first_column_of_the_layout_and_the_text_the_second()
    {
        using var ctx = new BunitContext();
        var doc = LegalMarkdown.Parse("---\nversion: 1.0\ndate: 2026-11-17\ndraft: false\n---\n# Regulamin\n\n## 1. Ogólne\n\ntekst\n\n## 2. Płatność\n\ntekst");
        var cut = ctx.Render<LegalPage>(p => p.Add(c => c.Title, "Regulamin").Add(c => c.Doc, doc));
        var columns = cut.Find(".legal__layout").Children;
        Assert.Equal(["NAV", "DIV"], columns.Select(c => c.TagName));
        Assert.Contains("contents", columns[0].ClassList);
        Assert.Contains("legal__body", columns[1].ClassList);
        Assert.Single(cut.FindAll("h1"));
    }

    [Fact]
    public void The_legal_contents_list_sticks_only_from_56_rem()
    {
        // The sticky rule lives in the 56 rem media block of the scoped sheet; the phone layout is a plain block.
        var css = File.ReadAllText(Path.Combine(Repo.Web, "Components", "Pages", "Static", "LegalPage.razor.css"));
        var wide = css[css.IndexOf("@media (min-width: 56rem)", StringComparison.Ordinal)..];
        Assert.Contains(".contents { position: sticky;", wide);
        Assert.DoesNotContain("position: sticky", css[..css.IndexOf("@media (min-width: 56rem)", StringComparison.Ordinal)]);
    }

    [Fact]
    public async Task The_404_has_one_h1_the_status_and_the_search_form()
    {
        await using var web = PublicSupport.Host();
        var page = await web.NewBrowser().GetAsync("/nie-ma-takiej-strony");
        Assert.Equal(HttpStatusCode.NotFound, page.Status); // the existing status behaviour
        Assert.Single(page.All("h1"));
        Assert.Equal("Nie znaleziono strony", page.TextOf("h1"));
        Assert.Equal("noindex,follow", page.One("meta[name=robots]")!.GetAttribute("content"));

        var form = page.One("[data-testid=page-not-found] form[role=search]")!;
        Assert.Equal("/programisci", form.GetAttribute("action"));
        Assert.Equal("get", form.GetAttribute("method")!.ToLowerInvariant());
        var input = form.QuerySelector("input")!;
        Assert.Equal("q", input.GetAttribute("name"));
        Assert.Equal("Czego szukasz?", form.QuerySelector($"label[for='{input.Id}']")!.TextContent);
        Assert.Equal("Szukaj", form.QuerySelector("button")!.TextContent.Trim());
    }

    [Fact]
    public async Task With_the_directory_down_the_404_keeps_the_search_and_shows_no_category_links()
    {
        await using var web = PublicSupport.Host(FakeBackend.Throw(new HttpRequestException("down")));
        var page = await web.NewBrowser().GetAsync("/nie-ma-takiej-strony");
        Assert.Equal(HttpStatusCode.NotFound, page.Status);
        Assert.NotNull(page.One("form[role=search][action='/programisci']"));
        Assert.Empty(page.All("[data-testid=page-not-found] .chip"));
        Assert.Null(page.One("[data-testid=page-not-found] h2"));
    }

    [Fact]
    public async Task The_error_page_has_one_h1_a_retry_link_and_a_way_home()
    {
        await using var web = PublicSupport.Host();
        var page = await web.NewBrowser().GetAsync("/Error");
        Assert.Single(page.All("h1"));
        Assert.Equal("Coś poszło nie tak", page.TextOf("h1"));
        var links = page.All("[role=alert] a");
        Assert.Equal(["Spróbuj ponownie", "Wróć na stronę główną"], links.Select(a => a.TextContent.Trim()));
        Assert.All(links, a => Assert.Equal("/", a.GetAttribute("href")));
    }
}

public class StaticPageComponentTests
{
    [Fact]
    public void A_scroll_table_takes_a_shared_table_class_and_stays_a_labelled_focusable_region()
    {
        using var ctx = new BunitContext();
        var cut = ctx.Render<ScrollTable>(p => p.Add(c => c.Caption, "Wagi").Add(c => c.TableClass, "table--cards")
            .AddChildContent("<tbody><tr><td data-label=\"Waga\">45%</td></tr></tbody>"));
        Assert.Contains("table--cards", cut.Find("table").ClassList);
        var wrap = cut.Find(".table-wrap");
        Assert.Equal("0", wrap.GetAttribute("tabindex"));
        Assert.Equal("region", wrap.GetAttribute("role"));
        Assert.Contains("Wagi", wrap.GetAttribute("aria-label"));
        Assert.Equal("Waga", cut.Find("td").GetAttribute("data-label"));
    }

    [Fact]
    public void A_scroll_table_without_a_class_renders_a_plain_table()
    {
        using var ctx = new BunitContext();
        var cut = ctx.Render<ScrollTable>(p => p.Add(c => c.Caption, "Stałe").AddChildContent("<tbody><tr><td>5</td></tr></tbody>"));
        Assert.False(cut.Find("table").HasAttribute("class"));
    }
}
