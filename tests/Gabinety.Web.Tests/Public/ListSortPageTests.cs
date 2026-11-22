using System.Net;
using Gabinety.Web.Api;

namespace Gabinety.Web.Tests.Public;

/// <summary>T-11.05 on the real host: the order control, its links, the Directory call it makes, the cards' technologies and next free time, and the SEO of a sorted page.</summary>
public class ListSortPageTests
{
    static WebFactory Host(int items = 2, bool next = false, PractitionerCardDto? first = null) => PublicSupport.Host(r => r.RequestUri!.AbsolutePath switch
    {
        "/api/v1/practitioners" => FakeBackend.Json(first is null
            ? Sample.Cards(items, next ? "NEXT" : null)
            : new PageDto<PractitionerCardDto>([first, .. Sample.Cards(items - 1).Items], next ? "NEXT" : null)),
        _ => PublicSupport.DefaultDirectory()(r),
    });

    static string LastListCall(WebFactory web) => web.Directory.Requests.Last(r => r.PathAndQuery.StartsWith("/api/v1/practitioners?", StringComparison.Ordinal) || r.PathAndQuery == "/api/v1/practitioners").PathAndQuery;

    [Fact]
    public async Task The_order_control_is_a_select_in_a_get_form_that_applies_on_change_with_a_button_for_no_script()
    {
        await using var web = Host();
        var page = await web.NewBrowser().GetAsync("/programisci/audyt-kodu?duration=30");
        var form = page.One("form[data-testid=sort]")!;
        Assert.Equal("get", form.GetAttribute("method"));
        Assert.Equal("/programisci/audyt-kodu", form.GetAttribute("action"));
        var select = form.QuerySelector("select[name=sortuj]")!;
        Assert.Equal("autosubmit", select.GetAttribute("data-enhance"));
        Assert.Equal(["trafnosc", "cena-rosnaco", "cena-malejaco", "termin"], form.QuerySelectorAll("option").Select(o => o.GetAttribute("value")));
        Assert.Equal("trafnosc", form.QuerySelector("option[selected]")!.GetAttribute("value")); // the default is selected, and nothing is written to the URL
        Assert.Equal("30", form.QuerySelector("input[name=duration]")!.GetAttribute("value")); // the other filters ride along
        Assert.Null(form.QuerySelector("input[name=cursor]")); // a cursor belongs to its order: the form never carries one
        Assert.Equal("Sortuj", form.QuerySelector("noscript button")?.TextContent.Trim());
    }

    [Fact]
    public async Task Choosing_the_cheapest_first_asks_the_Directory_for_price_asc_and_marks_it_current()
    {
        await using var web = Host();
        var page = await web.NewBrowser().GetAsync("/programisci?sortuj=cena-rosnaco");
        Assert.Contains("sort=price_asc", LastListCall(web));
        Assert.Equal("cena-rosnaco", page.One("form[data-testid=sort] option[selected]")!.GetAttribute("value"));
    }

    [Fact]
    public async Task The_next_free_time_and_the_dearest_first_orders_reach_the_Directory_under_their_wire_names()
    {
        await using var next = Host();
        await next.NewBrowser().GetAsync("/programisci?sortuj=termin");
        Assert.Contains("sort=next_slot", LastListCall(next));
        await using var dear = Host();
        await dear.NewBrowser().GetAsync("/programisci?sortuj=cena-malejaco");
        Assert.Contains("sort=price_desc", LastListCall(dear));
    }

    [Fact]
    public async Task An_unknown_order_is_the_plain_list_and_the_default_is_never_sent()
    {
        await using var web = Host();
        var page = await web.NewBrowser().GetAsync("/programisci?sortuj=nie-ma");
        Assert.DoesNotContain("sort", LastListCall(web));
        Assert.Equal("trafnosc", page.One("form[data-testid=sort] option[selected]")!.GetAttribute("value"));
    }

    [Fact]
    public async Task Every_pagination_link_and_filter_link_keeps_the_order()
    {
        await using var web = Host(2, next: true);
        var page = await web.NewBrowser().GetAsync("/programisci/audyt-kodu?sortuj=termin&duration=60");
        Assert.Contains("sortuj=termin", page.One("a[rel=next]")!.GetAttribute("href"));
        Assert.Contains("cursor=NEXT", page.One("a[rel=next]")!.GetAttribute("href"));
        Assert.NotEmpty(page.All("nav[aria-label=Filtry] a.chip"));
        Assert.All(page.All("nav[aria-label=Filtry] a.chip"), a => Assert.Contains("sortuj=termin", a.GetAttribute("href")));
        Assert.Equal("termin", page.One("form.search-form input[name=sortuj]")!.GetAttribute("value")); // the search keeps the order as a hidden field
    }

    [Fact]
    public async Task A_sorted_page_is_noindex_and_its_canonical_is_the_plain_list()
    {
        await using var web = Host();
        var page = await web.NewBrowser().GetAsync("/programisci/audyt-kodu?sortuj=cena-rosnaco");
        Assert.Equal("noindex,follow", page.One("meta[name=robots]")!.GetAttribute("content"));
        Assert.Equal("http://localhost/programisci/audyt-kodu", page.One("link[rel=canonical]")!.GetAttribute("href"));
    }

    [Fact]
    public async Task A_card_shows_its_technologies_and_its_next_free_time_and_a_card_without_one_shows_neither()
    {
        var with = new PractitionerCardDto(Guid.NewGuid(), "demo-anna-demowska", "Anna Demowska", "Audyty kodu .NET", null, ["email", "github", "approved"], null, true,
            new MoneyDto(25000, "PLN"), ["audyt-kodu"], [30, 60], ["dotnet", "react"], new DateTimeOffset(2026, 11, 24, 8, 30, 0, TimeSpan.Zero));
        await using var web = Host(first: with);
        var page = await web.NewBrowser().GetAsync("/programisci");
        var first = page.All("[data-testid=practitioner-card]")[0];
        Assert.Equal([".NET", "React"], first.QuerySelectorAll(".practitioner-card__techs .badge--neutral").Select(b => b.TextContent));
        Assert.Equal("Najbliższy termin: jutro, 09:30", first.QuerySelector(".practitioner-card__next")!.TextContent.Trim());
        var second = page.All("[data-testid=practitioner-card]")[1];
        Assert.Null(second.QuerySelector(".practitioner-card__next"));
        Assert.Null(second.QuerySelector(".practitioner-card__techs"));
    }

    [Fact]
    public async Task The_clearing_of_filters_keeps_the_chosen_order()
    {
        await using var web = Host(0, next: false);
        var page = await web.NewBrowser().GetAsync("/programisci?q=zzzzzz&sortuj=termin");
        Assert.Equal("/programisci?sortuj=termin", page.One(".empty-state a.btn--ghost")!.GetAttribute("href"));
    }
}
