using System.Net;
using System.Text.Json;
using Gabinety.Web.Api;

namespace Gabinety.Web.Tests.PractitionerAreaTests;

/// <summary>The contract availability form of the profile page (ADR proposal 0013): its own form, its own save, its own errors; the profile form is untouched.</summary>
public class ContractFormHostTests
{
    const string Path = "/gabinet-programisty/profil";
    static KeyValuePair<string, string> F(string name, string value) => KeyValuePair.Create(name, value);
    static string Today(PractitionerWorld w, int days) => w.Today.AddDays(days).ToString("yyyy-MM-dd");

    [Fact]
    public async Task The_form_is_its_own_section_with_the_labels_the_state_and_one_main_button()
    {
        var (web, browser, world) = await PractitionerBrowsers.SignedIn(w => w.HaveProfile());
        await using var _ = web;
        var page = await browser.GetAsync(Path);
        var section = page.One("[data-testid=contract-form]")!;
        Assert.Equal("Współpraca kontraktowa", section.QuerySelector("h2")!.TextContent);
        Assert.Equal(["Dostępność", "Dostępny od (opcjonalnie)", "Godzin tygodniowo (opcjonalnie)", "Orientacyjna stawka za godzinę (zł) (opcjonalnie)"], section.QuerySelectorAll("label").Select(l => Page.Squash(l.TextContent)));
        Assert.All(section.QuerySelectorAll("label"), l => Assert.NotNull(page.One($"#{l.GetAttribute("for")}")));
        Assert.Equal(["Nie przyjmuję propozycji", "Jestem dostępny od zaraz", "Będę dostępny od wskazanego dnia"], section.QuerySelectorAll("option").Select(o => o.TextContent));
        Assert.Equal(["not_available"], section.QuerySelectorAll("option[selected]").Select(o => o.GetAttribute("value")));
        Assert.Equal("Ustaw dostępność", section.QuerySelector("button[type=submit]")!.TextContent);
        Assert.Equal("/gabinet-programisty/propozycje", section.QuerySelector("a")!.GetAttribute("href"));
        Assert.Equal("date", page.One("#contractFrom")!.GetAttribute("type"));
        // the journeys click the profile's button by the name "Zapisz" (a substring match): no other button may contain it
        Assert.Single(page.All("form button[type=submit]"), b => b.TextContent.Contains("Zapisz"));
        Assert.Equal(2, page.All("form[method=post]").Count);
    }

    [Fact]
    public async Task What_the_service_holds_is_what_the_form_shows()
    {
        var (web, browser, _) = await PractitionerBrowsers.SignedIn(w => { w.HaveProfile(); w.Contract = new("available_from", new DateOnly(2027, 2, 23), 20, 15050); });
        await using var _ = web;
        var page = await browser.GetAsync(Path);
        Assert.Equal(["available_from"], page.All("#contractStatus option[selected]").Select(o => o.GetAttribute("value")));
        Assert.Equal(("2027-02-23", "20", "150,50"), (page.One("#contractFrom")!.GetAttribute("value")!, page.One("#contractHours")!.GetAttribute("value")!, page.One("#contractRate")!.GetAttribute("value")!));
    }

    [Fact]
    public async Task A_valid_form_is_saved_and_the_page_says_so_in_the_one_fixed_flash()
    {
        var (web, browser, world) = await PractitionerBrowsers.SignedIn(w => w.HaveProfile());
        await using var _ = web;
        var done = await browser.Submit(Path, "contract", [F("contractStatus", "available_from"), F("contractFrom", Today(world, 90)), F("contractHours", "24"), F("contractRate", "110")]);
        Assert.Equal(HttpStatusCode.Found, done.Status);
        Assert.Equal(Path + "?flash=saved", done.Location);
        Assert.Equal(new ContractAvailabilitySettingsDto("available_from", world.Today.AddDays(90), 24, 11000), world.Contract);
        var again = await browser.GetAsync(Path);
        Assert.Equal(["available_from"], again.All("#contractStatus option[selected]").Select(o => o.GetAttribute("value")));
        Assert.Equal("24", again.One("#contractHours")!.GetAttribute("value"));
    }

    [Fact]
    public async Task Available_now_sends_no_date_and_not_available_sends_nothing_else()
    {
        var (web, browser, world) = await PractitionerBrowsers.SignedIn(w => { w.HaveProfile(); w.Contract = new("available_now", null, 10, null); });
        await using var _ = web;
        await browser.Submit(Path, "contract", [F("contractStatus", "available_now"), F("contractFrom", Today(world, 90)), F("contractHours", "10"), F("contractRate", "")]);
        Assert.Equal(new ContractAvailabilitySettingsDto("available_now", null, 10, null), world.Contract); // a date typed for another status is ignored
        var off = await browser.Submit(Path, "contract", [F("contractStatus", "not_available"), F("contractHours", "garbage")]);
        Assert.Equal(HttpStatusCode.Found, off.Status);
        Assert.Equal(new ContractAvailabilitySettingsDto("not_available", null, null, null), world.Contract);
    }

    [Fact]
    public async Task Every_problem_is_shown_at_once_and_nothing_is_sent()
    {
        var (web, browser, world) = await PractitionerBrowsers.SignedIn(w => w.HaveProfile());
        await using var _ = web;
        var page = await browser.Submit(Path, "contract", [F("contractStatus", "available_from"), F("contractFrom", ""), F("contractHours", "0"), F("contractRate", "5")]);
        Assert.Equal(HttpStatusCode.OK, page.Status);
        var summary = page.One("[data-testid=form-summary]")!;
        Assert.Equal(["#contractFrom", "#contractHours", "#contractRate"], summary.QuerySelectorAll("li a").Select(a => a.GetAttribute("href")));
        Assert.Equal("Popraw 3 pola", summary.QuerySelector("h2")!.TextContent);
        Assert.All(new[] { "contractFrom", "contractHours", "contractRate" }, id => Assert.NotNull(page.One($"#{id}-error")));
        Assert.Equal(["available_from"], page.All("#contractStatus option[selected]").Select(o => o.GetAttribute("value"))); // what was chosen stays
        Assert.Equal("5", page.One("#contractRate")!.GetAttribute("value"));
        Assert.Equal(0, world.ContractSaves);
    }

    [Fact]
    public async Task A_field_the_service_refuses_is_shown_where_it_belongs_and_a_service_that_is_down_is_an_alert()
    {
        var (web, browser, world) = await PractitionerBrowsers.SignedIn(w => { w.HaveProfile(); w.ContractRefusal = (HttpStatusCode.UnprocessableEntity, "validation_failed", ["rateMinor"]); });
        await using var _ = web;
        var fields = new[] { F("contractStatus", "available_now"), F("contractHours", "10"), F("contractRate", "100") };
        var refused = await browser.Submit(Path, "contract", fields);
        Assert.Equal(["#contractRate"], refused.All("[data-testid=form-summary] li a").Select(a => a.GetAttribute("href")));
        Assert.DoesNotContain("SERVER-", refused.Html);

        world.ContractRefusal = (HttpStatusCode.ServiceUnavailable, "dependency_unavailable", null);
        var down = await browser.Submit(Path, "contract", fields);
        Assert.Equal(HttpStatusCode.OK, down.Status);
        Assert.Equal("alert", down.One("[data-testid=contract-error]")!.GetAttribute("role"));
        Assert.Contains("Nie udało się zapisać dostępności", down.TextOf("[data-testid=contract-error]"));
        Assert.DoesNotContain("SERVER-", down.Html);
    }

    [Fact]
    public async Task The_profile_form_still_saves_on_its_own_and_leaves_the_availability_alone()
    {
        var (web, browser, world) = await PractitionerBrowsers.SignedIn(w => { w.HaveProfile(); w.Contract = new("available_now", null, 12, null); });
        await using var _ = web;
        var saved = await browser.Submit(Path, "profile", [F("displayName", "Demo Programista"), F("headline", "Testy end to end dla zespołów"), F("bio", "Opis demo.")]);
        Assert.Equal(HttpStatusCode.Found, saved.Status);
        Assert.Equal(0, world.ContractSaves); // the profile post does not touch the availability
        Assert.Equal("available_now", world.Contract.Status);
    }

    [Fact]
    public async Task A_service_that_does_not_know_the_resource_leaves_the_form_at_not_available()
    {
        var (web, browser, world) = await PractitionerBrowsers.SignedIn(w => w.HaveProfile());
        await using var _ = web;
        world.ContractRefusal = null;
        var page = await browser.GetAsync(Path);
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal(["not_available"], page.All("#contractStatus option[selected]").Select(o => o.GetAttribute("value")));
    }
}
