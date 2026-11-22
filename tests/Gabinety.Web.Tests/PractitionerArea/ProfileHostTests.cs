using System.Net;

namespace Gabinety.Web.Tests.PractitionerAreaTests;

public class ProfileHostTests
{
    static readonly string Bio = new('b', 220);

    [Fact]
    public async Task The_form_has_the_labels_the_journey_finds_each_exactly_once_and_the_counters_inside_the_fields()
    {
        var (web, browser, _) = await PractitionerBrowsers.SignedIn(w => w.HaveProfile());
        await using var _ = web;
        var page = await browser.GetAsync("/gabinet-programisty/profil");
        Assert.Equal("Profil", page.TextOf("h1"));
        var labels = page.All("form label").Select(l => l.TextContent.Trim()).ToList();
        Assert.Equal(["Nazwa wyświetlana", "Nagłówek", "Opis (o mnie)", "Link do portfolio (opcjonalnie)", "Domyślny link do spotkań (opcjonalnie)", "NIP (opcjonalnie)"], labels);
        // J2: getByLabel is a substring match: each of these must match ONE control.
        foreach (var needle in new[] { "Nagłówek", "Opis", "Link do portfolio", "NIP", "Nazwa wyświetlana" }) Assert.Equal(1, labels.Count(l => l.Contains(needle, StringComparison.OrdinalIgnoreCase)));
        Assert.All(page.All("form label"), l => Assert.NotNull(page.One($"#{l.GetAttribute("for")}"))); // a label points at a real control
        Assert.Equal("Zapisz", page.One("form button[type=submit]")!.TextContent); // J2
        Assert.Equal("0 / 120", page.TextOf("#headline ~ .field__counter")); // inside the field, right after the control
        Assert.Equal("numeric", page.One("#nip")!.GetAttribute("inputmode"));
        Assert.Equal("10", page.One("#nip")!.GetAttribute("maxlength"));
        Assert.Equal("textarea", page.One("#bio")!.LocalName);
    }

    [Fact]
    public async Task The_nip_field_takes_digits_and_exactly_one_label_on_the_page_contains_opis()
    {
        var (web, browser, _) = await PractitionerBrowsers.SignedIn(w => w.HaveProfile());
        await using var _ = web;
        var page = await browser.GetAsync("/gabinet-programisty/profil");
        var nip = page.One("#nip")!;
        Assert.Equal(("numeric", "10", "off"), (nip.GetAttribute("inputmode"), nip.GetAttribute("maxlength"), nip.GetAttribute("autocomplete")));
        // J2 / contract 9.5: getByLabel("Opis") is a substring match over every label on the page, not only the form's
        Assert.Equal(["Opis (o mnie)"], page.All("label").Select(l => l.TextContent.Trim()).Where(t => t.Contains("opis", StringComparison.OrdinalIgnoreCase)).ToList());
    }

    [Fact]
    public async Task The_preview_beside_the_form_shows_the_saved_name_headline_and_first_lines_of_the_bio()
    {
        var (web, browser, world) = await PractitionerBrowsers.SignedIn(w =>
        {
            w.HaveProfile();
            w.Profile = w.Profile! with { Headline = "Zapisany nagłówek", Bio = "Pierwsza linia.\nDruga linia.\nTrzecia linia.\nCzwarta linia." };
        });
        await using var _ = web;
        var page = await browser.GetAsync("/gabinet-programisty/profil");
        var preview = page.One(".profile-preview")!;
        Assert.Equal("Podgląd profilu", preview.QuerySelector("#profile-preview-title")!.TextContent);
        Assert.Equal("Demo Programista", preview.QuerySelector(".profile-preview__name")!.TextContent);
        Assert.Equal("Zapisany nagłówek", preview.QuerySelector(".profile-preview__headline")!.TextContent);
        Assert.Contains("Trzecia linia.", preview.QuerySelector(".profile-preview__bio")!.TextContent);
        Assert.Empty(preview.QuerySelectorAll("h1, h2, h3")); // paragraphs, not headings: the page keeps one h1 and its heading order
        Assert.Null(page.One("form .profile-preview")); // beside the form, not inside it
        Assert.Equal("grid-2", page.One("form")!.ParentElement!.ClassName);
        Assert.NotNull(world.Profile);
    }

    [Fact]
    public async Task Saving_sends_the_trimmed_fields_omits_empty_optional_ones_and_redirects_with_the_flash()
    {
        var (web, browser, world) = await PractitionerBrowsers.SignedIn(w => w.HaveProfile());
        await using var _ = web;
        var response = await browser.Submit("/gabinet-programisty/profil", "profile", [new("headline", "  Testy end to end dla zespołów  "), new("bio", Bio), new("portfolioUrl", "https://demo.example.com/"), new("meetingUrlDefault", ""), new("nip", "5260250274")]);
        Assert.Equal("/gabinet-programisty/profil?flash=saved", response.Location);
        var put = web.Directory.Requests.Single(r => r.Method == HttpMethod.Put);
        Assert.StartsWith("Bearer ", put.Authorization);
        Assert.DoesNotContain("meetingUrlDefault", put.Body); // omitted, not null
        Assert.Equal("Testy end to end dla zespołów", System.Text.Json.Nodes.JsonNode.Parse(put.Body!)!["headline"]!.GetValue<string>()); // trimmed
        Assert.True(world.Profile!.NipValid);
        var after = await browser.GetAsync(response.Location!);
        Assert.Equal("Zapisano.", after.TextOf("[data-testid=flash]")); // J2: getByText("Zapisano.")
        Assert.Equal(1, after.ExactText("Zapisano."));
        Assert.NotNull(after.One("[data-testid=flash] button[data-enhance=dismiss][hidden][aria-label]")); // UX-060: shown by enhance.js only; no text, so the live region's text is the message
        Assert.Equal("Testy end to end dla zespołów", after.One("#headline")!.GetAttribute("value"));
    }

    [Fact]
    public async Task Every_error_is_shown_at_once_in_a_summary_with_links_and_under_each_field_and_the_typed_values_stay()
    {
        var (web, browser, _) = await PractitionerBrowsers.SignedIn(w => w.HaveProfile());
        await using var _ = web;
        var response = await browser.Submit("/gabinet-programisty/profil", "profile", [new("displayName", "D"), new("headline", "krótki"), new("nip", "1234567890")]);
        Assert.Equal(HttpStatusCode.OK, response.Status);
        var summary = response.One("[data-testid=form-summary]")!;
        Assert.Equal("alert", summary.GetAttribute("role"));
        Assert.Equal("Popraw 3 pola", summary.QuerySelector("h2")!.TextContent);
        Assert.Equal(["#displayName", "#headline", "#nip"], summary.QuerySelectorAll("a").Select(a => a.GetAttribute("href")));
        foreach (var id in new[] { "displayName", "headline", "nip" })
        {
            var input = response.One($"#{id}")!;
            Assert.Equal("true", input.GetAttribute("aria-invalid"));
            Assert.Contains($"{id}-error", input.GetAttribute("aria-describedby"));
            Assert.StartsWith("Błąd:", response.TextOf($"#{id}-error"));
        }
        Assert.Equal("krótki", response.One("#headline")!.GetAttribute("value"));
        Assert.DoesNotContain("SERVER-", response.Html);
    }

    [Fact]
    public async Task A_contact_detail_is_reported_on_the_field_that_has_it_and_nowhere_else()
    {
        var (web, browser, _) = await PractitionerBrowsers.SignedIn(w => w.HaveProfile());
        await using var _ = web;
        var response = await browser.Submit("/gabinet-programisty/profil", "profile", [new("headline", "Testy end to end dla zespołów"), new("bio", "Napisz do mnie: jan@example.com")]);
        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Contains("Usuń dane kontaktowe", response.TextOf("#bio-error"));
        Assert.Null(response.One("#headline-error"));
        Assert.Equal("Napisz do mnie: jan@example.com", response.One("#bio")!.TextContent.Trim()); // kept for the user to fix
    }

    [Fact]
    public async Task An_account_with_no_profile_is_sent_to_the_dashboard_that_onboards_it()
    {
        var (web, browser, _) = await PractitionerBrowsers.SignedIn();
        await using var _ = web;
        foreach (var path in new[] { "/gabinet-programisty/profil", "/gabinet-programisty/oferty", "/gabinet-programisty/dostepnosc" })
        {
            var page = await browser.GetAsync(path);
            Assert.Contains((int)page.Status, new[] { 302, 303 });
            Assert.Equal("/gabinet-programisty", page.Location);
        }
    }

    [Fact]
    public async Task A_visitor_is_sent_to_sign_in_from_every_page_of_the_area_and_the_service_down_is_a_503()
    {
        await using var anonymous = new WebFactory();
        var visitor = anonymous.NewBrowser();
        foreach (var path in new[] { "/gabinet-programisty/profil", "/gabinet-programisty/oferty", "/gabinet-programisty/dostepnosc", "/gabinet-programisty/platnosci", "/practitioner/payments" })
        {
            var page = await visitor.GetAsync(path);
            Assert.Equal(HttpStatusCode.Found, page.Status);
            Assert.StartsWith("/logowanie?redirect=", page.Location);
        }
        var (web, browser, _) = await PractitionerBrowsers.SignedIn(w => { w.HaveProfile(); w.DirectoryDown = true; });
        await using var _ = web;
        var down = await browser.GetAsync("/gabinet-programisty/profil");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, down.Status);
        Assert.Equal("alert", down.One("[data-testid=unavailable]")!.GetAttribute("role"));
    }

    [Fact]
    public async Task The_pages_of_the_area_carry_the_section_tabs_with_the_current_one_marked_and_are_not_for_crawlers()
    {
        var (web, browser, _) = await PractitionerBrowsers.SignedIn(w => w.HaveProfile());
        await using var _ = web;
        var page = await browser.GetAsync("/gabinet-programisty/profil");
        var nav = page.One("nav.tabs")!;
        Assert.Equal("Sekcje panelu programisty", nav.GetAttribute("aria-label"));
        Assert.Equal(["Start", "Profil", "Oferty", "Dostępność", "Płatności"], nav.QuerySelectorAll("a").Select(a => a.TextContent));
        Assert.Equal(["Profil"], nav.QuerySelectorAll("a[aria-current=page]").Select(a => a.TextContent));
        Assert.Equal("noindex,nofollow", page.One("meta[name=robots]")!.GetAttribute("content"));
        var start = await browser.GetAsync("/gabinet-programisty");
        Assert.Equal(["Start"], start.One("nav.tabs")!.QuerySelectorAll("a[aria-current=page]").Select(a => a.TextContent));
    }
}
