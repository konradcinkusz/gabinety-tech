using System.Net;
using System.Text.Json;
using System.Xml.Linq;
using Gabinety.Web.Api;

namespace Gabinety.Web.Tests.Public;

/// <summary>The wave-2 public pages as a visitor with no JavaScript gets them: complete HTML in the first response, the statuses, the hooks of DESIGN-CONTRACT section 9.</summary>
public class ProfilePageTests
{
    static async Task<Page> Get(WebFactory web, string path = "/gabinet/demo-anna-demowska") => await web.NewBrowser().GetAsync(path);

    [Fact]
    public async Task The_profile_has_its_content_the_hooks_and_the_structured_data_in_the_first_response()
    {
        await using var web = PublicSupport.Host();
        var page = await Get(web);
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal("Anna Ąćęł Demowska", page.TextOf("h1")); Assert.Single(page.All("h1"));
        Assert.Equal(["O mnie", "Oferta", "Opinie"], page.All("h2").Select(h => h.TextContent));
        Assert.Equal(["Audyt kodu .NET", "Krótka konsultacja"], page.All("li h3").Select(h => h.TextContent)); // getByRole("heading", {level: 3}) inside the offer's listitem
        var offer = page.All("ul.offerings > li")[0];
        Assert.Equal($"/rezerwacja/{PublicSupport.OfferA}?gabinet=demo-anna-demowska", offer.QuerySelector("a[aria-label='Zarezerwuj: Audyt kodu .NET']")!.GetAttribute("href"));
        var ld = page.All("script[type='application/ld+json']"); // exactly one, with the display name (journey J1)
        Assert.Single(ld);
        Assert.Contains("Anna Ąćęł Demowska", ld[0].TextContent);
        Assert.Equal("ProfessionalService", JsonDocument.Parse(ld[0].TextContent).RootElement.GetProperty("@type").GetString());
        Assert.Equal("Anna Ąćęł Demowska: konsultacje online | Gabinety Tech", page.Dom.Title);
        Assert.Equal("http://localhost/gabinet/demo-anna-demowska", page.One("link[rel=canonical]")!.GetAttribute("href"));
        Assert.Equal("index,follow", page.One("meta[name=robots]")!.GetAttribute("content"));
        Assert.Contains("Audyty kodu .NET", page.One("meta[name=description]")!.GetAttribute("content"));
        Assert.Equal("reviews-title", page.One("section#opinie")!.GetAttribute("aria-labelledby")); // named by its heading: getByRole("region", {name: "Opinie"})
        Assert.Equal("Opinie", page.TextOf("#reviews-title"));
    }

    [Fact]
    public async Task Slot_times_are_links_named_by_the_time_inside_the_offer_and_lead_to_the_booking_flow()
    {
        await using var web = PublicSupport.Host();
        var page = await Get(web);
        var offer = page.All("ul.offerings > li")[0];
        var times = offer.QuerySelectorAll("a.slot");
        Assert.All(times, t => Assert.Matches(@"^\d{2}:\d{2}$", t.TextContent)); // the harness regex
        Assert.Equal(["10:00", "11:00"], times.Select(t => t.TextContent)); // 09:00 and 10:00 UTC on 24 Nov are Warsaw winter time
        Assert.Equal($"/rezerwacja/{PublicSupport.OfferA}?start=2026-11-24T09%3A00%3A00Z&gabinet=demo-anna-demowska", times[0].GetAttribute("href"));
        Assert.Equal("wtorek, 24 listopada", offer.QuerySelector(".slot-days a[aria-current=true]")!.TextContent); // F-08
        Assert.Contains("środa, 25 listopada", offer.QuerySelectorAll(".slot-days a").Select(a => a.TextContent));
        Assert.Contains("Europe/Warsaw", offer.TextContent);
    }

    [Fact]
    public async Task A_chosen_day_shows_its_times_and_more_days_asks_for_the_next_window()
    {
        await using var web = PublicSupport.Host();
        var page = await Get(web, $"/gabinet/demo-anna-demowska?oferta={PublicSupport.OfferA}&dzien=2026-11-25");
        Assert.Equal(["14:00"], page.All("ul.offerings > li")[0].QuerySelectorAll("a.slot").Select(t => t.TextContent));
        var more = page.All("ul.offerings > li")[0].QuerySelectorAll("a.btn").First(a => a.TextContent == "Pokaż kolejne dni");
        Assert.Equal($"/gabinet/demo-anna-demowska?okna=2&oferta={PublicSupport.OfferA}&dzien=2026-11-25#oferta-{PublicSupport.OfferA}", more.GetAttribute("href"));
        var paged = PublicSupport.Host(); var page2 = await Get(paged, "/gabinet/demo-anna-demowska?okna=3");
        Assert.Equal(6, paged.Booking.Requests.Count(r => r.PathAndQuery.Contains("/slots?"))); // 2 offers x 3 windows of 14 days
        Assert.Contains(paged.Booking.Requests, r => r.PathAndQuery.EndsWith($"&from=2026-12-21&to=2027-01-03") || r.PathAndQuery.Contains("from=2026-12-21&to=2027-01-03"));
        await paged.DisposeAsync(); _ = page2;
    }

    [Fact]
    public async Task The_slot_reads_are_public_and_carry_no_bearer()
    {
        await using var web = PublicSupport.Host();
        await Get(web);
        Assert.All(web.Booking.Requests, r => Assert.Null(r.Authorization));
        Assert.StartsWith($"/api/v1/practitioners/{PublicSupport.PractitionerId}/slots?offeringId={PublicSupport.OfferA}&from=", web.Booking.Requests[0].PathAndQuery);
    }

    [Fact]
    public async Task F16_no_offers_no_times_and_times_unavailable_are_three_different_sentences()
    {
        await using var none = PublicSupport.Host(PublicSupport.DefaultDirectory(PublicSupport.Profile(offerings: [])));
        var noOffers = await Get(none);
        Assert.Equal("Ten programista nie ma teraz aktywnych ofert.", noOffers.TextOf("[data-testid=no-offerings]"));
        Assert.Null(noOffers.One("[data-testid=slots-empty]")); Assert.Null(noOffers.One("[data-testid=slots-unavailable]"));

        await using var empty = PublicSupport.Host(booking: _ => FakeBackend.Json(new SlotsDto([])));
        var noTimes = await Get(empty);
        Assert.Contains("Brak wolnych terminów w najbliższych 14 dniach", noTimes.TextOf("[data-testid=slots-empty]"));
        Assert.Null(noTimes.One("[data-testid=no-offerings]"));

        await using var down = PublicSupport.Host(booking: FakeBackend.Throw(new HttpRequestException("down")));
        var failed = await Get(down);
        Assert.Equal(HttpStatusCode.OK, failed.Status); // the rest of the page stays
        Assert.Contains("Nie możemy teraz pokazać terminów", failed.TextOf("[data-testid=slots-unavailable]"));
        Assert.Equal("status", failed.One("[data-testid=slots-unavailable]")!.GetAttribute("role")); // a warning is announced politely
        Assert.Equal("Spróbuj ponownie", failed.All("ul.offerings > li")[0].QuerySelector("[data-testid=slots-unavailable] + a")!.TextContent);
        Assert.Equal("Anna Ąćęł Demowska", failed.TextOf("h1"));
        Assert.Null(failed.One("[data-testid=no-offerings]"));
    }

    [Fact]
    public async Task F08_member_since_reads_od_listopada_and_the_facts_are_separate_items()
    {
        await using var web = PublicSupport.Host();
        var page = await Get(web);
        Assert.Equal(["Zrealizowane konsultacje: 4", "Na platformie od listopada 2026"], page.All("ul.facts li").Select(l => l.TextContent));
        Assert.DoesNotContain("Listopada", page.Text); // no capitalised month anywhere
    }

    [Fact]
    public async Task Reviews_show_text_as_text_the_reply_and_a_more_link_and_one_empty_sentence()
    {
        var review = new ReviewDto(Guid.NewGuid(), 5, "<b>Świetnie</b> & szybko", new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero), new ReplyDto("Dziękuję!", new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero)));
        await using var web = PublicSupport.Host(reputation: r => FakeBackend.Json(new PageDto<ReviewDto>([review], "CUR2")));
        var page = await Get(web, "/gabinet/demo-anna-demowska?opinie=CUR1");
        Assert.Equal($"/api/v1/practitioners/{PublicSupport.PractitionerId}/reviews?cursor=CUR1", web.Reputation.Requests[0].PathAndQuery);
        var item = page.One("section#opinie li")!;
        Assert.Contains("Zweryfikowany klient · Ocena: 5 z 5 · 20 listopada 2026", item.TextContent);
        Assert.Contains("<b>Świetnie</b> & szybko", item.TextContent);
        Assert.Null(item.QuerySelector("b")); // review text is never markup
        Assert.Contains("Dziękuję!", item.QuerySelector(".reply")!.TextContent);
        Assert.Equal("/gabinet/demo-anna-demowska?opinie=CUR2#opinie", page.One("section#opinie a.btn")!.GetAttribute("href"));

        await using var none = PublicSupport.Host();
        var empty = await Get(none);
        Assert.Single(empty.All("section#opinie p")); // P-PROF-1: one sentence, not two
        Assert.Equal("Nowy na platformie: ten programista nie ma jeszcze opinii.", empty.TextOf("section#opinie p"));
    }

    [Fact]
    public async Task A_reviews_outage_is_a_warning_in_that_region_only()
    {
        await using var web = PublicSupport.Host(reputation: FakeBackend.Throw(new HttpRequestException("down")));
        var page = await Get(web);
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Contains("Opinie są chwilowo niedostępne", page.TextOf("[data-testid=reviews-unavailable]"));
        Assert.NotEmpty(page.All("a.slot"));
    }

    [Fact]
    public async Task Links_are_https_only_and_external_ones_carry_rel()
    {
        await using var web = PublicSupport.Host(PublicSupport.DefaultDirectory(PublicSupport.Profile(portfolio: "javascript:alert(1)")));
        var page = await Get(web);
        Assert.Null(page.One("a[href^=javascript]"));
        Assert.Null(page.One("a[href*='demo.example']"));
        var github = page.One("a[href='https://github.com/demo-anna']")!;
        Assert.Contains("nofollow", github.GetAttribute("rel"));
        await using var ok = PublicSupport.Host();
        Assert.Equal("noopener nofollow ugc", (await Get(ok)).One("a[href='https://demo.example/portfolio']")!.GetAttribute("rel"));
    }

    [Fact]
    public async Task F09_an_unknown_profile_is_a_404_that_says_programmer_not_page_and_offers_the_way_on()
    {
        await using var web = PublicSupport.Host();
        var page = await Get(web, "/gabinet/nie-ma-takiego");
        Assert.Equal(HttpStatusCode.NotFound, page.Status);
        Assert.Equal("Nie znaleziono programisty", page.TextOf("h1")); Assert.Single(page.All("h1"));
        Assert.Equal("/programisci", page.One("[data-testid=profile-not-found] a.btn")!.GetAttribute("href"));
        Assert.Equal(["Audyt kodu", "Migracja"], page.All("[data-testid=profile-not-found] .chip").Select(c => c.TextContent));
        Assert.Equal("noindex,follow", page.One("meta[name=robots]")!.GetAttribute("content"));
        Assert.Null(page.One("script[type='application/ld+json']"));
    }

    [Fact]
    public async Task A_profile_the_directory_calls_gone_is_a_410()
    {
        await using var web = PublicSupport.Host(r => r.RequestUri!.AbsolutePath.StartsWith("/api/v1/practitioners/") ? FakeBackend.Problem(HttpStatusCode.Gone, "gone") : PublicSupport.DefaultDirectory()(r));
        var page = await Get(web);
        Assert.Equal(HttpStatusCode.Gone, page.Status);
        Assert.Equal("Nie znaleziono programisty", page.TextOf("h1"));
    }

    [Fact]
    public async Task A_directory_that_is_down_is_a_503_not_a_200_error_page()
    {
        await using var web = PublicSupport.Host(FakeBackend.Throw(new HttpRequestException("down")));
        var page = await Get(web);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, page.Status);
        Assert.Equal("alert", page.One("[data-testid=unavailable]")!.GetAttribute("role"));
        Assert.Equal("noindex,follow", page.One("meta[name=robots]")!.GetAttribute("content"));
        Assert.DoesNotContain("SERVER-", page.Html);
    }
}

public class StaticPagesTests
{
    [Theory]
    [InlineData("/jak-to-dziala", "Jak to działa")] [InlineData("/cennik", "Cennik")] [InlineData("/zasady-rankingu", "Zasady rankingu")]
    [InlineData("/regulamin", "Regulamin")] [InlineData("/polityka-prywatnosci", "Polityka prywatności")]
    public async Task A_static_page_has_one_h1_a_unique_title_and_description_and_a_canonical(string path, string h1)
    {
        await using var web = PublicSupport.Host();
        var page = await web.NewBrowser().GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal(h1, page.TextOf("h1")); Assert.Single(page.All("h1"));
        Assert.EndsWith("| Gabinety Tech", page.Dom.Title);
        Assert.NotEmpty(page.One("meta[name=description]")!.GetAttribute("content")!);
        Assert.Equal("http://localhost" + path, page.One("link[rel=canonical]")!.GetAttribute("href"));
        Assert.Empty(page.SetCookies);
    }

    [Fact]
    public async Task Pricing_shows_the_rate_and_the_example_split_and_the_rate_is_configuration()
    {
        await using var web = PublicSupport.Host();
        var page = await web.NewBrowser().GetAsync("/cennik");
        Assert.Contains("Prowizja platformy to 15% ceny", page.Text);
        var cells = page.All("tbody td").Select(c => c.TextContent.Replace(' ', ' '));
        Assert.Equal(["250,00 zł", "37,50 zł", "212,50 zł"], cells);
        await using var ten = PublicSupport.Host(); ten.With("Billing:FirstConsultationFeeBps", "1000");
        var other = await ten.NewBrowser().GetAsync("/cennik");
        Assert.Contains("Prowizja platformy to 10% ceny", other.Text);
        Assert.Equal(["250,00 zł", "25,00 zł", "225,00 zł"], other.All("tbody td").Select(c => c.TextContent.Replace(' ', ' ')));
    }

    [Fact]
    public async Task F12_the_ranking_page_shows_every_value_the_total_row_and_a_keyboard_reachable_scroller()
    {
        await using var web = PublicSupport.Host();
        var page = await web.NewBrowser().GetAsync("/zasady-rankingu");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal("82,70", page.TextOf("[data-testid=ranking-total]")); // the Wynik razem row has its value
        Assert.Equal("Wynik razem", page.One("tfoot th")!.TextContent);
        var constants = page.All("section[aria-labelledby=rank-constants] tbody tr").Select(r => r.QuerySelector("td")!.TextContent);
        Assert.Equal(["5", "3,8", "3", "10"], constants); // the Stałe table shows values
        Assert.Equal(["45%", "25%", "15%", "15%"], page.All("section[aria-labelledby=rank-weights] tbody td.num").Select(c => c.TextContent));
        Assert.Contains("Wersja zasad: 2026-11-v1", page.Text);
        Assert.Contains("Obowiązuje od: 13.11.2026", page.Text);
        var scrollers = page.All(".table-wrap");
        Assert.Equal(3, scrollers.Count);
        Assert.All(scrollers, s => { Assert.Equal("0", s.GetAttribute("tabindex")); Assert.Equal("region", s.GetAttribute("role")); Assert.False(string.IsNullOrWhiteSpace(s.GetAttribute("aria-label"))); });
    }

    [Fact]
    public async Task The_ranking_page_is_a_503_and_never_shows_stale_numbers_when_the_directory_is_down()
    {
        await using var web = PublicSupport.Host(FakeBackend.Throw(new HttpRequestException("down")));
        var page = await web.NewBrowser().GetAsync("/zasady-rankingu");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, page.Status);
        Assert.Equal("Zasady rankingu", page.TextOf("h1"));
        Assert.Empty(page.All("table"));
        Assert.Equal("alert", page.One("[data-testid=unavailable]")!.GetAttribute("role"));
    }

    [Fact]
    public async Task Legal_pages_render_the_owners_text_with_a_contents_list_and_the_draft_notice()
    {
        await using var web = PublicSupport.Host();
        var page = await web.NewBrowser().GetAsync("/regulamin");
        Assert.Contains("Wersja robocza", page.TextOf("[data-testid=draft-notice]"));
        Assert.Equal(["1. Postanowienia ogólne", "2. Rezerwacja i płatność", "3. Spory", "4. Prowizja"], page.All("nav.contents a").Select(a => a.TextContent));
        Assert.Equal("s-1-postanowienia-ogolne", page.One("nav.contents a")!.GetAttribute("href")![1..]);
        Assert.NotNull(page.One("h3#s-1-postanowienia-ogolne"));
        Assert.Empty(page.All("script:not([src])")); // the only script is the framework's own file
    }

    [Fact]
    public async Task The_home_page_has_tiles_the_trust_row_and_still_the_hooks()
    {
        await using var web = PublicSupport.Host();
        var page = await web.NewBrowser().GetAsync("/");
        Assert.Equal(["Audyt kodu", "Migracja"], page.All("[data-testid=categories] a").Select(a => a.TextContent));
        Assert.Equal(2, page.All("[data-testid=categories] li.card").Count);
        Assert.Contains("Zweryfikowanych programistów: 6", page.TextOf("[data-testid=trust]"));
        Assert.Contains("Płatność z góry, programista dostaje pieniądze po konsultacji", page.TextOf("[data-testid=trust]"));
        Assert.Equal(3, page.All("ol.steps > li").Count);
        Assert.Equal("Szukaj", page.One("form[role=search] button")!.TextContent); // the field and the button in one form row (G-7)
    }
}

public class ListPageFixesTests
{
    static WebFactory Host(int items, bool next, bool known = true) => PublicSupport.Host(r => r.RequestUri!.AbsolutePath switch
    {
        "/api/v1/practitioners" => FakeBackend.Json(Sample.Cards(items, next ? "NEXT" : null)),
        _ => PublicSupport.DefaultDirectory()(r),
    });

    [Fact]
    public async Task F05_an_empty_filtered_result_says_zero_and_never_the_categorys_total()
    {
        await using var web = Host(0, false);
        var page = await web.NewBrowser().GetAsync("/programisci?q=zzzzzz");
        Assert.Equal("Wyniki: 0", page.TextOf("[data-testid=count]"));
        Assert.Contains("Brak programistów dla wybranych filtrów", page.TextOf("[data-testid=empty]"));
        Assert.Equal("/programisci", page.One(".empty-state a.btn")!.GetAttribute("href"));
        Assert.Empty(page.All("[data-testid=practitioner-list]"));
    }

    [Fact]
    public async Task F05_the_count_of_a_filtered_whole_result_is_its_items_and_an_open_ended_one_states_none()
    {
        await using var whole = Host(2, false);
        Assert.Equal("Wyniki: 2", (await whole.NewBrowser().GetAsync("/programisci?duration=30")).TextOf("[data-testid=count]"));
        await using var more = Host(2, true);
        Assert.Null((await more.NewBrowser().GetAsync("/programisci?duration=30")).One("[data-testid=count]"));
        await using var plain = Host(2, true);
        Assert.Equal("Wyniki: 6", (await plain.NewBrowser().GetAsync("/programisci")).TextOf("[data-testid=count]")); // unfiltered: the sitemap's count
    }

    [Fact]
    public async Task P_LIST_the_filters_are_one_disclosure_with_segmented_duration_and_price_and_a_breadcrumb_on_a_category()
    {
        await using var web = Host(2, false);
        var page = await web.NewBrowser().GetAsync("/programisci/audyt-kodu/dotnet?duration=30");
        Assert.Equal("Filtry (3)", page.TextOf("details.filters > summary")); // problem, technology and duration are active
        Assert.NotNull(page.One("details.filters[open] nav[aria-label=Filtry]")); // the navigation keeps its name (hook)
        Assert.Equal(2, page.All("nav[aria-label=Filtry] ul.segmented").Count);
        Assert.Equal(["Programiści", "Audyt kodu", ".NET"], page.All("nav.breadcrumb li").Select(l => l.TextContent.Trim()));
        Assert.Equal("page", page.One("nav.breadcrumb [aria-current]")!.GetAttribute("aria-current"));
    }

    [Fact]
    public async Task P_LIST_2_a_card_shows_one_verified_badge_not_four_and_the_rating_as_a_star()
    {
        await using var web = PublicSupport.Host(r => r.RequestUri!.AbsolutePath == "/api/v1/practitioners"
            ? FakeBackend.Json(new PageDto<PractitionerCardDto>([Sample.Card(rating: new RatingDto(4.8, 25), isNew: false)], null)) : PublicSupport.DefaultDirectory()(r));
        var card = (await web.NewBrowser().GetAsync("/programisci")).One("[data-testid=practitioner-card]")!;
        Assert.Equal(["Zweryfikowany", "★ 4,8 (25)"], card.QuerySelectorAll(".badge").Select(b => b.TextContent));
    }
}

public class SeoEndpointsTests
{
    [Fact]
    public async Task The_sitemap_lists_every_approved_practitioner_and_only_the_indexable_categories()
    {
        await using var web = PublicSupport.Host();
        var page = await web.NewBrowser().GetAsync("/sitemap.xml");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.StartsWith("application/xml", page.Header("Content-Type"));
        var ns = XNamespace.Get("http://www.sitemaps.org/schemas/sitemap/0.9");
        var urls = XDocument.Parse(page.Html).Root!.Elements(ns + "url").ToList(); // parses: well-formed XML
        Assert.Equal(["http://localhost/programisci", "http://localhost/programisci/audyt-kodu", "http://localhost/gabinet/demo-anna-demowska", "http://localhost/gabinet/demo-bartek"], urls.Select(u => u.Element(ns + "loc")!.Value));
        Assert.DoesNotContain("/programisci/migracja", page.Html); // 2 practitioners: noindex, so not offered
        Assert.Equal("2026-11-20T08:30:00.000Z", urls[2].Element(ns + "lastmod")!.Value);
        Assert.Null(urls[0].Element(ns + "lastmod"));
    }

    [Fact]
    public void The_sitemap_escapes_what_a_slug_could_carry()
    {
        var xml = SeoContentProbe.Sitemap(new SitemapDto([], [new("a&b<c>", DateTimeOffset.UnixEpoch)]));
        Assert.NotNull(XDocument.Parse(xml)); // still well-formed
        Assert.DoesNotContain("a&b<c>", xml);
    }

    [Fact]
    public async Task A_directory_that_does_not_answer_is_a_503_never_an_empty_sitemap()
    {
        await using var web = PublicSupport.Host(FakeBackend.Throw(new HttpRequestException("down")));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await web.NewBrowser().GetAsync("/sitemap.xml")).Status);
    }

    [Fact]
    public async Task Robots_has_the_rules_of_the_Next_app_and_points_at_the_sitemap()
    {
        await using var web = PublicSupport.Host();
        var page = await web.NewBrowser().GetAsync("/robots.txt");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.StartsWith("text/plain", page.Header("Content-Type"));
        Assert.Equal(["User-Agent: *", "Allow: /", "Disallow: /moje", "Disallow: /gabinet-programisty", "Disallow: /admin", "Disallow: /rezerwacja", "Disallow: /checkout", "Disallow: /api", "", "Sitemap: http://localhost/sitemap.xml", ""],
            page.Html.Split('\n'));
        Assert.Empty(page.SetCookies);
    }

    [Fact]
    public void The_disallowed_paths_are_exactly_those_of_the_Next_app()
    {
        // web/src/lib/seo/sitemap.ts DISALLOWED_PATHS: the two hosts tell a crawler the same thing.
        var ts = File.ReadAllText(Path.Combine(Repo.Web, "..", "..", "web", "src", "lib", "seo", "sitemap.ts"));
        var listed = System.Text.RegularExpressions.Regex.Match(ts, @"DISALLOWED_PATHS = \[(.*?)\]").Groups[1].Value.Split(',').Select(s => s.Trim().Trim('"')).ToArray();
        Assert.Equal(listed, Gabinety.Web.Hosting.SeoContent.DisallowedPaths);
    }
}

static class SeoContentProbe
{
    public static string Sitemap(SitemapDto dto) => Gabinety.Web.Hosting.SeoContent.Sitemap("http://localhost", dto);
}
