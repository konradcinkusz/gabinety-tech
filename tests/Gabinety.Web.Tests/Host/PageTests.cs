using System.Net;
using Gabinety.Web.Api;

namespace Gabinety.Web.Tests.Pipeline;

/// <summary>The public pages as a crawler and a visitor without JavaScript get them: complete HTML from the first response (journey J1 asserts this for the profile; the same is true here).</summary>
public class PageTests
{
    static WebFactory Backends()
    {
        var web = new WebFactory();
        web.Directory.Respond = r => r.RequestUri!.AbsolutePath switch
        {
            "/api/v1/taxonomy" => FakeBackend.Json(Sample.Taxonomy),
            "/api/v1/sitemap" => FakeBackend.Json(Sample.Sitemap(6, true)),
            "/api/v1/practitioners" => FakeBackend.Json(Sample.Cards(2, "NEXT")),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };
        return web;
    }

    [Fact]
    public async Task The_home_page_has_its_content_in_the_first_response_with_no_script_needed()
    {
        await using var web = Backends();
        var page = await web.NewBrowser().GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal("Umów płatną konsultację ze zweryfikowanym programistą", page.TextOf("h1"));
        Assert.Single(page.All("h1")); // one h1 (SPEC 5 rule 14)
        Assert.Equal(["Audyt kodu", "Migracja"], page.All("[data-testid=categories] a").Select(a => a.TextContent));
        Assert.Equal("/programisci/audyt-kodu", page.One("[data-testid=categories] a")!.GetAttribute("href"));
        var search = page.One("form[role=search]")!;
        Assert.Equal(("get", "/programisci"), (search.GetAttribute("method"), search.GetAttribute("action")));
        Assert.Equal("q", search.QuerySelector("input")!.GetAttribute("name"));
        Assert.Equal("home-q", search.QuerySelector("label")!.GetAttribute("for")); // every form control has a label
        Assert.Equal("Wyszukiwarka programistów", search.GetAttribute("aria-label"));
        Assert.Equal("Gabinety Tech: płatna konsultacja ze zweryfikowanym programistą", page.Dom.Title);
        Assert.Equal("http://localhost/", page.One("link[rel=canonical]")!.GetAttribute("href"));
        Assert.Equal("pl", page.Dom.DocumentElement.GetAttribute("lang"));
    }

    [Fact]
    public async Task A_failing_directory_is_an_error_state_of_the_categories_region_and_the_rest_of_the_home_page_stays()
    {
        await using var web = new WebFactory();
        web.Directory.Respond = FakeBackend.Throw(new HttpRequestException("down"));
        var page = await web.NewBrowser().GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal("Umów płatną konsultację ze zweryfikowanym programistą", page.TextOf("h1"));
        Assert.Contains("Nie udało się teraz wczytać kategorii", page.TextOf("[data-testid=categories-error]"));
        Assert.Equal("alert", page.One("[data-testid=categories-error]")!.GetAttribute("role"));
        Assert.NotNull(page.One("form[role=search]")); // the search still works without the Directory
        Assert.Equal("/programisci", page.One("section a[href='/programisci']")!.GetAttribute("href"));
    }

    [Fact]
    public async Task The_list_renders_what_journey_J1_looks_for_by_role_and_name()
    {
        await using var web = Backends();
        var page = await web.NewBrowser().GetAsync("/programisci");
        Assert.Equal(HttpStatusCode.OK, page.Status);

        Assert.Equal("Programiści", page.TextOf("h1"));
        Assert.Equal("Wyniki: 6", Page.Squash(page.TextOf("[data-testid=count]"))); // the page object reads /^Wyniki: \d+$/ (getByText, and visible)
        Assert.Equal("6 programistów", page.TextOf(".result-summary__total")); // the plural line is its own element
        var region = page.One("section[aria-label='Lista programistów']")!; // getByRole("region", { name: "Lista programistów" })
        var cards = region.QuerySelectorAll("ul[data-testid=practitioner-list] > li"); // the cards hold lists of their own: only the direct children of the results list (the page object does the same)
        Assert.Equal(2, cards.Length);
        var link = region.QuerySelector("h2 > a")!; // getByRole("link", { name: displayName, exact: true })
        Assert.Equal(("Osoba Demo 1", "/gabinet/demo-person-1"), (link.TextContent, link.GetAttribute("href")));
        Assert.Equal(2, page.All("[data-testid=practitioner-card]").Count);

        var nav = page.One("nav[aria-label=Filtry]")!; // CategoryNav: getByRole("navigation", { name: "Filtry" }).getByRole("link", { name, exact: true })
        Assert.Equal("/programisci/audyt-kodu", nav.QuerySelectorAll("a").First(a => a.TextContent == "Audyt kodu").GetAttribute("href"));
        Assert.Equal("true", nav.QuerySelectorAll("a").First(a => a.TextContent == "Wszystkie").GetAttribute("aria-current"));
        Assert.Contains("od 250\u00A0zł", page.Dom.QuerySelector("[data-testid=practitioner-card]")!.TextContent); // whole amounts have no grosz
        Assert.Contains("30 min, 60 min", page.Dom.QuerySelector("[data-testid=practitioner-card]")!.TextContent);
        Assert.Contains("Nowy", page.Dom.QuerySelector("[data-testid=practitioner-card]")!.TextContent);
        Assert.Equal("Audyt kodu", page.TextOf("[data-testid=practitioner-card] .practitioner-card__categories")); // the list passes the taxonomy's names down
    }

    [Fact]
    public async Task The_state_of_a_list_lives_in_the_url_and_reaches_the_directory()
    {
        await using var web = Backends();
        var page = await web.NewBrowser().GetAsync("/programisci/audyt-kodu/dotnet?duration=30&maxPriceMinor=30000&q=%20kod%20&cursor=CUR&ignored=1");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        var call = web.Directory.Requests.Last(r => r.PathAndQuery.StartsWith("/api/v1/practitioners"));
        Assert.Equal("/api/v1/practitioners?problem=audyt-kodu&tech=dotnet&q=kod&duration=30&maxPriceMinor=30000&cursor=CUR", call.PathAndQuery);
    }

    [Fact]
    public async Task A_category_page_filters_and_pages_with_plain_links()
    {
        await using var web = Backends();
        web.Directory.Respond = r => r.RequestUri!.AbsolutePath switch
        {
            "/api/v1/taxonomy" => FakeBackend.Json(Sample.Taxonomy),
            "/api/v1/sitemap" => FakeBackend.Json(Sample.Sitemap(5, true, "/programisci/audyt-kodu")),
            _ => FakeBackend.Json(Sample.Cards(1, "NEXT")),
        };
        var page = await web.NewBrowser().GetAsync("/programisci/audyt-kodu?duration=60&cursor=CUR");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal("Audyt kodu", page.TextOf("h1")); // the page object: expect(list.heading()).toHaveText(namePl)
        Assert.Null(page.One("[data-testid=count]")); // F-05: a filtered, paged variant has no total, so it states none (the sitemap's 5 is the unfiltered category)
        var next = page.One("nav[aria-label=Paginacja] a[rel=next]")!;
        Assert.Equal("/programisci/audyt-kodu?duration=60&cursor=NEXT", next.GetAttribute("href"));
        Assert.Equal("/programisci/audyt-kodu?duration=60", page.One("nav[aria-label=Paginacja] a[rel=prev]")!.GetAttribute("href")); // cursors are forward-only: back means the first page
        var form = page.One("nav[aria-label=Filtry] form[role=search]")!;
        Assert.Equal("/programisci/audyt-kodu", form.GetAttribute("action"));
        Assert.Equal("60", form.QuerySelector("input[name=duration]")!.GetAttribute("value")); // the other filters ride along
        Assert.Equal("noindex,follow", page.One("meta[name=robots]")!.GetAttribute("content")); // a variant is never indexed
        Assert.Equal("http://localhost/programisci/audyt-kodu", page.One("link[rel=canonical]")!.GetAttribute("href"));
    }

    [Fact]
    public async Task A_category_with_fewer_than_three_practitioners_is_noindex_and_a_full_one_is_indexed()
    {
        foreach (var (count, indexable, robots) in new[] { (2, false, "noindex,follow"), (3, true, "index,follow") })
        {
            await using var web = Backends();
            web.Directory.Respond = r => r.RequestUri!.AbsolutePath switch
            {
                "/api/v1/taxonomy" => FakeBackend.Json(Sample.Taxonomy),
                "/api/v1/sitemap" => FakeBackend.Json(Sample.Sitemap(count, indexable, "/programisci/migracja")),
                _ => FakeBackend.Json(Sample.Cards(count)),
            };
            var page = await web.NewBrowser().GetAsync("/programisci/migracja");
            Assert.Equal(robots, page.One("meta[name=robots]")!.GetAttribute("content"));
            Assert.Equal($"Wyniki: {count}", Page.Squash(page.TextOf("[data-testid=count]")));
        }
    }

    [Fact]
    public async Task An_empty_result_names_the_active_filters_as_removable_chips_and_states_no_list()
    {
        await using var web = new WebFactory();
        web.Directory.Respond = r => r.RequestUri!.AbsolutePath switch
        {
            "/api/v1/taxonomy" => FakeBackend.Json(Sample.Taxonomy),
            "/api/v1/sitemap" => FakeBackend.Json(Sample.Sitemap()),
            _ => FakeBackend.Json(Sample.Cards(0)),
        };
        var page = await web.NewBrowser().GetAsync("/programisci/audyt-kodu?duration=30&q=kod");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal("Nie ma programistów dla tych filtrów", page.TextOf("[data-testid=empty] h2"));
        Assert.Equal("Wyniki: 0", Page.Squash(page.TextOf("[data-testid=count]")));
        Assert.Equal("0 programistów", page.TextOf(".result-summary__total"));
        Assert.Null(page.One("[data-testid=practitioner-list]")); // an empty list is never rendered
        var chips = page.All("[data-testid=empty] [role=group] a.chip").Select(a => (a.TextContent.Replace("(usuń filtr)", "").Trim(), a.GetAttribute("href"))).ToList();
        Assert.Equal([("30 min ×", "/programisci/audyt-kodu?q=kod"), ("Szukaj: „kod” ×", "/programisci/audyt-kodu?duration=30")], chips.Select(c => (Page.Squash(c.Item1), c.Item2))); // the category is the page, not a chip
        Assert.Equal("/programisci/audyt-kodu", page.One("[data-testid=empty] a.btn--ghost")!.GetAttribute("href")); // Wyczyść filtry keeps the category
    }

    [Fact]
    public async Task An_empty_category_without_filters_has_no_chips_and_no_clear_link()
    {
        await using var web = new WebFactory();
        web.Directory.Respond = r => r.RequestUri!.AbsolutePath switch
        {
            "/api/v1/taxonomy" => FakeBackend.Json(Sample.Taxonomy),
            "/api/v1/sitemap" => FakeBackend.Json(Sample.Sitemap(0, false, "/programisci/migracja")),
            _ => FakeBackend.Json(Sample.Cards(0)),
        };
        var page = await web.NewBrowser().GetAsync("/programisci/migracja");
        Assert.Equal("Nie ma programistów dla tych filtrów", page.TextOf("[data-testid=empty] h2"));
        Assert.Empty(page.All("[data-testid=empty] a.chip"));
        Assert.Null(page.One("[data-testid=empty] a.btn--ghost"));
    }

    [Theory]
    [InlineData("/programisci/nie-ma-takiej")] [InlineData("/programisci/audyt-kodu/nie-ma")]
    public async Task An_unknown_category_is_a_404_page_not_an_empty_list(string path)
    {
        await using var web = Backends();
        var page = await web.NewBrowser().GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, page.Status);
        Assert.Equal("Nie znaleziono strony", page.TextOf("h1"));
    }

    [Fact]
    public async Task A_cursor_the_directory_refuses_is_a_404()
    {
        await using var web = Backends();
        web.Directory.Respond = r => r.RequestUri!.AbsolutePath switch
        {
            "/api/v1/taxonomy" => FakeBackend.Json(Sample.Taxonomy),
            "/api/v1/sitemap" => FakeBackend.Json(Sample.Sitemap()),
            _ => FakeBackend.Problem(HttpStatusCode.UnprocessableEntity, "invalid_input"),
        };
        Assert.Equal(HttpStatusCode.NotFound, (await web.NewBrowser().GetAsync("/programisci?cursor=forged")).Status);
    }

    [Fact]
    public async Task A_down_directory_gives_the_list_an_alert_and_a_503_so_a_crawler_does_not_index_an_error_page()
    {
        await using var web = new WebFactory();
        web.Directory.Respond = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);
        var page = await web.NewBrowser().GetAsync("/programisci");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, page.Status);
        Assert.Equal("Programiści", page.TextOf("h1"));
        Assert.Contains("Nie udało się teraz wczytać listy", page.TextOf("[role=alert]"));
        Assert.Equal("noindex,follow", page.One("meta[name=robots]")!.GetAttribute("content"));
        Assert.NotNull(page.One("nav a[href='/programisci']")); // the shell still renders
    }

    [Fact]
    public async Task The_error_text_of_a_failing_backend_never_reaches_the_page()
    {
        await using var web = new WebFactory();
        web.Directory.Respond = _ => FakeBackend.Problem(HttpStatusCode.BadGateway, "dependency_unavailable");
        var page = await web.NewBrowser().GetAsync("/programisci");
        Assert.DoesNotContain("SERVER-TITLE-MUST-NOT-APPEAR", page.Html);
        Assert.DoesNotContain("SERVER-DETAIL-MUST-NOT-APPEAR", page.Html);
    }

    [Fact]
    public async Task User_content_from_the_directory_is_rendered_as_text()
    {
        await using var web = Backends();
        web.Directory.Respond = r => r.RequestUri!.AbsolutePath switch
        {
            "/api/v1/taxonomy" => FakeBackend.Json(Sample.Taxonomy),
            "/api/v1/sitemap" => FakeBackend.Json(Sample.Sitemap()),
            _ => FakeBackend.Json(new PageDto<PractitionerCardDto>([Sample.Card(name: "<img src=x onerror=alert(1)>", headline: "<script>alert(2)</script>")], null)),
        };
        var page = await web.NewBrowser().GetAsync("/programisci");
        Assert.DoesNotContain("<img src=x", page.Html);
        Assert.DoesNotContain("<script>alert", page.Html);
        Assert.Equal("<img src=x onerror=alert(1)>", page.TextOf("h2 a"));
    }

    [Fact]
    public async Task The_visitors_address_goes_to_the_directory_from_a_static_page()
    {
        await using var web = Backends();
        await web.NewBrowser().GetAsync("/programisci", ("X-Forwarded-For", "203.0.113.5, 198.51.100.77"));
        Assert.All(web.Directory.Requests, r => { Assert.Equal("198.51.100.77", r.ClientAddress); Assert.Equal(WebFactory.TrustedKey, r.EdgeKey); });
        Assert.NotEmpty(web.Directory.Requests);
    }

    [Fact]
    public async Task An_unknown_path_is_the_404_page()
    {
        await using var web = Backends();
        var page = await web.NewBrowser().GetAsync("/nie-ma-takiej-strony");
        Assert.Equal(HttpStatusCode.NotFound, page.Status);
        Assert.Equal("Nie znaleziono strony", page.TextOf("h1"));
        Assert.Equal("noindex,follow", page.One("meta[name=robots]")!.GetAttribute("content"));
    }
}
