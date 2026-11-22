using System.Net;
using System.Text.Json.Nodes;

namespace Gabinety.Web.Tests.PractitionerAreaTests;

public class OffersHostTests
{
    static PractitionerWorld.Offer Offer(string title, bool active = true, long price = 18000, int duration = 60) => new() { Title = title, ProblemSlug = "code-review", Duration = duration, Price = price, Active = active };

    static (string, string)[] Valid(string title = "Przegląd testów end to end", string price = "180") => [("title", title), ("price", price), ("problemSlug", "code-review")];

    static KeyValuePair<string, string>[] Fields(params (string, string)[] f) => [.. f.Select(x => KeyValuePair.Create(x.Item1, x.Item2))];

    [Fact]
    public async Task The_form_has_the_labels_the_journey_uses_a_select_whose_first_option_is_wybierz_and_a_group_of_technology_checkboxes()
    {
        var (web, browser, _) = await PractitionerBrowsers.SignedIn(w => w.HaveProfile());
        await using var _ = web;
        var page = await browser.GetAsync("/gabinet-programisty/oferty");
        Assert.Equal("Oferty", page.TextOf("h1"));
        var labels = page.All("form[data-testid], form.form label, .field label").Select(l => l.TextContent.Trim()).ToList();
        foreach (var name in new[] { "Nazwa oferty", "Opis oferty", "Cena (zł)", "Rodzaj problemu" }) Assert.Equal(1, labels.Count(l => l.StartsWith(name))); // J2: getByLabel
        var problem = page.One("#problemSlug")!;
        Assert.Equal("Wybierz…", problem.QuerySelectorAll("option")[0].TextContent); // J2: selectOption({index: 1}) skips it
        Assert.Equal(["Przegląd kodu", "Migracja"], problem.QuerySelectorAll("option").Skip(1).Select(o => o.TextContent));
        var group = page.One("fieldset")!; // J2: getByRole("group", {name: /Technologie/}).getByRole("checkbox").first()
        Assert.Contains("Technologie", group.QuerySelector("legend")!.TextContent);
        Assert.Equal(5, group.QuerySelectorAll("input[type=checkbox]").Length);
        Assert.Equal("Zapisz", page.One("form.form button[type=submit]")!.TextContent);
        Assert.Equal("decimal", page.One("#price")!.GetAttribute("inputmode"));
    }

    [Fact]
    public async Task Saving_sends_the_price_as_integer_grosz_the_checked_technologies_and_redirects_with_the_flash_and_the_title_shows_once()
    {
        var (web, browser, world) = await PractitionerBrowsers.SignedIn(w => w.HaveProfile());
        await using var _ = web;
        var response = await browser.Submit("/gabinet-programisty/oferty", "offer", Fields(("title", "  Przegląd testów end to end  "), ("description", "Omawiamy scenariusze."), ("price", "180,50"), ("problemSlug", "code-review"), ("duration", "60")));
        var body = JsonNode.Parse(web.Directory.Requests.Single(r => r.Method == HttpMethod.Post && r.PathAndQuery.EndsWith("/offerings")).Body!)!;
        Assert.Equal(18050, body["priceMinor"]!.GetValue<long>()); // grosz, never a float
        Assert.Equal(60, body["durationMinutes"]!.GetValue<int>());
        Assert.Equal("Przegląd testów end to end", body["title"]!.GetValue<string>());
        Assert.Null(body["meetingUrl"]); // empty optional: omitted
        Assert.Equal("/gabinet-programisty/oferty?flash=saved", response.Location);
        Assert.Single(world.Offers);
        var after = await browser.GetAsync(response.Location!);
        Assert.Equal("Zapisano.", after.TextOf("[data-testid=flash]")); // J2
        Assert.Equal(1, after.ExactText("Przegląd testów end to end")); // J2: getByText(title) must be a single element
        Assert.Equal("180,50 zł", after.TextOf(".offer .num").Replace(' ', ' '));
    }

    [Fact]
    public async Task The_checked_technologies_travel_and_more_than_four_are_refused_inline_before_any_call()
    {
        var (web, browser, world) = await PractitionerBrowsers.SignedIn(w => w.HaveProfile());
        await using var _ = web;
        var four = await browser.Submit("/gabinet-programisty/oferty", "offer", Fields([.. Valid(), ("techs", "dotnet"), ("techs", "react")]));
        Assert.Equal(["dotnet", "react"], world.Offers.Single().Techs);
        Assert.Contains((int)four.Status, new[] { 302, 303 });
        var posts = web.Directory.Requests.Count(r => r.Method == HttpMethod.Post);
        var five = await browser.Submit("/gabinet-programisty/oferty", "offer", Fields([.. Valid("Druga oferta numer dwa"), ("techs", "dotnet"), ("techs", "react"), ("techs", "sql"), ("techs", "go"), ("techs", "rust")]));
        Assert.Equal(HttpStatusCode.OK, five.Status);
        Assert.Equal(posts, web.Directory.Requests.Count(r => r.Method == HttpMethod.Post)); // nothing sent
        Assert.Contains("najwyżej 4", five.TextOf("#techSlugs-error"));
        Assert.StartsWith("Błąd: ", five.One("#techSlugs-error")!.TextContent); // the hidden word is separated from the message
        Assert.Equal(5, five.All("input[type=checkbox]:checked").Count); // what the user ticked stays ticked
    }

    [Theory]
    [InlineData("250", true)] [InlineData("250,50", true)] [InlineData("250.5", true)] [InlineData("50", true)] [InlineData("1500", true)]
    [InlineData("49,99", false)] [InlineData("1500,01", false)] [InlineData("abc", false)] [InlineData("", false)] [InlineData("250,555", false)]
    public async Task The_price_is_validated_in_the_form_and_a_bad_one_never_reaches_the_service(string price, bool accepted)
    {
        var (web, browser, _) = await PractitionerBrowsers.SignedIn(w => w.HaveProfile());
        await using var _ = web;
        var response = await browser.Submit("/gabinet-programisty/oferty", "offer", Fields(Valid(price: price)));
        Assert.Equal(accepted, (int)response.Status is 302 or 303);
        Assert.Equal(accepted ? 1 : 0, web.Directory.Requests.Count(r => r.Method == HttpMethod.Post && r.PathAndQuery.EndsWith("/offerings")));
        if (!accepted)
        {
            Assert.Equal("true", response.One("#price")!.GetAttribute("aria-invalid"));
            Assert.Contains("price-error", response.One("#price")!.GetAttribute("aria-describedby"));
            Assert.Equal(price, response.One("#price")!.GetAttribute("value")); // the typed text stays
        }
    }

    [Fact]
    public async Task All_problems_come_at_once_in_the_summary_each_linked_to_its_field()
    {
        var (web, browser, _) = await PractitionerBrowsers.SignedIn(w => w.HaveProfile());
        await using var _ = web;
        var response = await browser.Submit("/gabinet-programisty/oferty", "offer", Fields(("title", "abc"), ("price", "x"), ("problemSlug", "")));
        var summary = response.One("[data-testid=form-summary]")!;
        Assert.Equal("Popraw 3 pola", summary.QuerySelector("h2")!.TextContent);
        Assert.Equal(["#title", "#price", "#problemSlug"], summary.QuerySelectorAll("a").Select(a => a.GetAttribute("href")));
    }

    [Fact]
    public async Task A_service_complaint_is_put_on_its_field_and_the_contact_rule_on_the_text_that_looks_like_contact()
    {
        var (web, browser, _) = await PractitionerBrowsers.SignedIn(w => w.HaveProfile());
        await using var _ = web;
        var response = await browser.Submit("/gabinet-programisty/oferty", "offer", Fields([.. Valid(), ("description", "Pisz: jan@example.com")]));
        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Contains("Usuń dane kontaktowe", response.TextOf("#description-error"));
        Assert.Null(response.One("#title-error"));
        Assert.DoesNotContain("SERVER-", response.Html);
    }

    [Fact]
    public async Task Disabling_an_offer_needs_a_confirmation_page_state_and_the_first_click_changes_nothing()
    {
        var (web, browser, world) = await PractitionerBrowsers.SignedIn(w => { w.HaveProfile(); w.Offers.Add(Offer("Przegląd testów")); });
        await using var _ = web;
        var id = world.Offers[0].Id;
        var list = await browser.GetAsync("/gabinet-programisty/oferty");
        var disable = list.All(".offer a").Single(a => a.TextContent == "Wyłącz");
        Assert.Equal($"/gabinet-programisty/oferty?wylacz={id}", disable.GetAttribute("href")); // a link (GET), not a button that acts
        Assert.Contains("btn--ghost-danger", disable.ClassName);
        Assert.Null(list.One("[data-testid=confirm-disable]"));

        var confirm = await browser.GetAsync(disable.GetAttribute("href")!);
        Assert.True(world.Offers[0].Active); // nothing happened yet
        Assert.DoesNotContain(web.Directory.Requests, r => r.PathAndQuery.EndsWith("deactivate"));
        var dialog = confirm.One("[data-testid=confirm-disable]")!;
        Assert.Equal("alertdialog", dialog.GetAttribute("role"));
        Assert.Equal("Wyłączyć ofertę „Przegląd testów”?", dialog.QuerySelector("h2")!.TextContent);
        Assert.Equal("confirm-title", dialog.GetAttribute("aria-labelledby"));
        Assert.Contains("Klienci nie zobaczą jej na profilu", dialog.TextContent);
        Assert.Equal("Tak, wyłącz ofertę", dialog.QuerySelector("button")!.TextContent);
        Assert.Contains("btn--danger", dialog.QuerySelector("button")!.ClassName);
        var cancel = dialog.QuerySelector("a")!;
        Assert.Equal(("Nie, zostaw aktywną", "/gabinet-programisty/oferty"), (cancel.TextContent, cancel.GetAttribute("href")));
    }

    [Fact]
    public async Task Confirming_disables_the_offer_with_a_flash_and_the_list_then_offers_wlacz_which_turns_it_back_on()
    {
        var (web, browser, world) = await PractitionerBrowsers.SignedIn(w => { w.HaveProfile(); w.Offers.Add(Offer("Przegląd testów")); });
        await using var _ = web;
        var id = world.Offers[0].Id;
        var done = await browser.Submit($"/gabinet-programisty/oferty?wylacz={id}", "offer-disable");
        Assert.Equal("/gabinet-programisty/oferty?flash=offer-disabled", done.Location);
        Assert.False(world.Offers[0].Active);
        var list = await browser.GetAsync(done.Location!);
        Assert.Equal("Oferta została wyłączona. Możesz ją włączyć z powrotem na liście.", list.TextOf("[data-testid=flash]"));
        var row = list.One(".offer")!;
        Assert.Equal("false", row.GetAttribute("data-active"));
        Assert.Contains("Wyłączona", row.TextContent);
        Assert.DoesNotContain(row.QuerySelectorAll("a"), a => a.TextContent == "Wyłącz"); // nothing left to disable
        var enable = row.QuerySelector("button")!;
        Assert.Equal("Włącz", enable.TextContent);

        var back = await browser.Submit("/gabinet-programisty/oferty", "offer-enable", Fields(("enable", id.ToString())));
        Assert.Equal("/gabinet-programisty/oferty?flash=offer-enabled", back.Location);
        Assert.True(world.Offers[0].Active);
    }

    [Fact]
    public async Task A_sixth_active_offer_cannot_be_turned_on_and_says_why()
    {
        var (web, browser, world) = await PractitionerBrowsers.SignedIn(w => { w.HaveProfile(); for (var i = 0; i < 5; i++) w.Offers.Add(Offer($"Oferta numer {i}")); w.Offers.Add(Offer("Wyłączona oferta", active: false)); });
        await using var _ = web;
        var off = world.Offers[5].Id;
        var page = await browser.Submit("/gabinet-programisty/oferty", "offer-enable", Fields(("enable", off.ToString())));
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Contains("najwyżej 5 aktywnych ofert", page.TextOf("[data-testid=offers-error]"));
        Assert.False(world.Offers[5].Active);
        Assert.True(page.One("form.form button[type=submit]")!.HasAttribute("disabled")); // adding is disabled and the reason is next to it
        Assert.Equal("Aktywne oferty: 5 z 5", page.TextOf("[data-testid=active-count]"));
    }

    [Fact]
    public async Task Editing_loads_the_offer_with_its_price_in_zloty_and_a_save_replaces_it()
    {
        var (web, browser, world) = await PractitionerBrowsers.SignedIn(w => { w.HaveProfile(); w.Offers.Add(Offer("Przegląd testów", price: 18050)); });
        await using var _ = web;
        var id = world.Offers[0].Id;
        var edit = await browser.GetAsync($"/gabinet-programisty/oferty?edit={id}");
        Assert.Equal("Edycja oferty", edit.TextOf("#offer-form-title"));
        Assert.Equal("180,50", edit.One("#price")!.GetAttribute("value"));
        Assert.Equal("60", edit.One("#duration option[selected]")!.GetAttribute("value"));
        Assert.Contains("tylko rezerwacji dokonanych po zapisie", edit.Text);
        var saved = await browser.Submit($"/gabinet-programisty/oferty?edit={id}", "offer", Fields(("title", "Przegląd testów v2"), ("price", "200")));
        Assert.Equal(HttpStatusCode.Found, saved.Status);
        Assert.Equal(("Przegląd testów v2", 20000L), (world.Offers[0].Title, world.Offers[0].Price));
        Assert.Contains(web.Directory.Requests, r => r.Method == HttpMethod.Put && r.PathAndQuery.EndsWith($"/offerings/{id}"));
    }

    [Fact]
    public async Task A_made_up_or_foreign_id_edits_nothing_and_an_empty_list_says_what_to_do()
    {
        var (web, browser, _) = await PractitionerBrowsers.SignedIn(w => w.HaveProfile());
        await using var _ = web;
        var page = await browser.GetAsync($"/gabinet-programisty/oferty?edit={Guid.NewGuid()}&wylacz={Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal("Nowa oferta", page.TextOf("#offer-form-title"));
        Assert.Null(page.One("[data-testid=confirm-disable]"));
        Assert.Contains("Nie masz jeszcze żadnej oferty", page.TextOf(".empty-state"));
        var junk = await browser.GetAsync("/gabinet-programisty/oferty?edit=not-a-guid");
        Assert.Equal(HttpStatusCode.OK, junk.Status);
    }

    [Fact]
    public async Task The_offer_text_is_text_and_a_failing_taxonomy_still_lets_the_page_render_with_a_message()
    {
        var (web, browser, _) = await PractitionerBrowsers.SignedIn(w => { w.HaveProfile(); w.Offers.Add(Offer("<b>Pogrubiona</b>")); });
        await using var _ = web;
        var page = await browser.GetAsync("/gabinet-programisty/oferty");
        Assert.Empty(page.All("b"));
        Assert.Equal("<b>Pogrubiona</b>", page.TextOf(".offer__title"));
    }
}
