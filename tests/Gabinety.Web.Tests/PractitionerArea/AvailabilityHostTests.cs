using System.Net;
using System.Text.Json.Nodes;

namespace Gabinety.Web.Tests.PractitionerAreaTests;

public class AvailabilityHostTests
{
    static KeyValuePair<string, string>[] Fields(params (string, string)[] f) => [.. f.Select(x => KeyValuePair.Create(x.Item1, x.Item2))];

    static async Task<Page> Post(Browser browser, params (string, string)[] fields)
    {
        var page = await browser.GetAsync("/gabinet-programisty/dostepnosc");
        var form = page.One("form[method=post]")!;
        // the draft's own hidden fields (rule-N-day, exception-N) are replaced by the fields each test posts; the rest (antiforgery) is sent as a browser would
        var hidden = form.QuerySelectorAll("input[type=hidden]").Where(i => !i.GetAttribute("name")!.StartsWith("rule-") && !i.GetAttribute("name")!.StartsWith("exception-")).Select(i => KeyValuePair.Create(i.GetAttribute("name")!, i.GetAttribute("value") ?? ""));
        var request = new HttpRequestMessage(HttpMethod.Post, "/gabinet-programisty/dostepnosc") { Content = new FormUrlEncodedContent(hidden.Concat(Fields(fields))) };
        return await browser.SendAsync(request);
    }

    [Fact]
    public async Task The_page_groups_the_windows_by_weekday_one_row_each_with_two_named_selects_a_hidden_day_and_a_remove_button()
    {
        var (web, browser, _) = await PractitionerBrowsers.SignedIn(w => { w.HaveProfile(); w.Rules.Add((3, "10:00", "11:30")); w.Rules.Add((1, "09:00", "12:00")); w.Exceptions.Add("2027-01-06"); });
        await using var _ = web;
        var page = await browser.GetAsync("/gabinet-programisty/dostepnosc");
        Assert.Equal("Dostępność", page.TextOf("h1"));
        Assert.Single(page.All("h1"));
        var days = page.All("[data-testid=day]");
        Assert.Equal(["poniedziałek", "wtorek", "środa", "czwartek", "piątek", "sobota", "niedziela"], days.Select(d => d.QuerySelector("h3")!.TextContent.Trim()));
        Assert.Equal([1, 0, 1, 0, 0, 0, 0], days.Select(d => d.QuerySelectorAll("[data-testid=rule]").Length));
        var rows = page.All("[data-testid=rule]");
        Assert.Equal(2, rows.Count);
        Assert.Equal("Przedział 1", rows[0].GetAttribute("aria-label"));
        Assert.Equal("1", rows[0].QuerySelector("input[type=hidden][name=rule-0-day]")!.GetAttribute("value")); // sorted: Monday first, the day is posted, not chosen
        Assert.Null(rows[0].QuerySelector("select[name=rule-0-day]"));
        Assert.Equal("Od, poniedziałek", rows[0].QuerySelector("select[name=rule-0-from]")!.GetAttribute("aria-label"));
        Assert.Equal("Do, poniedziałek", rows[0].QuerySelector("select[name=rule-0-to]")!.GetAttribute("aria-label"));
        Assert.Equal("Od, środa", rows[1].QuerySelector("select[name=rule-1-from]")!.GetAttribute("aria-label"));
        Assert.Equal(48, rows[0].QuerySelectorAll("select[name=rule-0-from] option").Count);
        Assert.Equal("09:00", rows[0].QuerySelector("select[name=rule-0-from] option[selected]")!.TextContent);
        Assert.Equal("Usuń", rows[0].QuerySelector("button")!.TextContent);
        Assert.All(days, d => Assert.Contains("Dodaj godziny", d.QuerySelector("button[value^='add:']")!.TextContent));
        Assert.Equal("Skopiuj na pozostałe dni robocze", days[0].QuerySelector("button[value='copy:1']")!.TextContent);
        Assert.Null(days[1].QuerySelector("button[value^='copy:']")); // nothing to copy from an empty day
        Assert.Null(days[5].QuerySelector("button[value^='copy:']")); // weekends are not a source
        Assert.Contains("Europe/Warsaw", page.TextOf(".lead"));
        Assert.Equal("save", page.One("form button[type=submit]")!.GetAttribute("value")); // Enter in a field saves; it never removes
        Assert.Equal("true", page.One("form button[type=submit]")!.GetAttribute("aria-hidden"));
    }

    [Fact]
    public async Task The_exception_is_listed_with_its_polish_long_date_and_the_posted_value_stays_the_iso_date()
    {
        var (web, browser, _) = await PractitionerBrowsers.SignedIn(w => { w.HaveProfile(); w.Exceptions.Add("2026-12-24"); });
        await using var _ = web;
        var page = await browser.GetAsync("/gabinet-programisty/dostepnosc");
        Assert.Equal("czwartek, 24 grudnia 2026", page.TextOf("[data-testid=exception]"));
        Assert.Equal("2026-12-24", page.One("input[type=hidden][name=exception-0]")!.GetAttribute("value"));
        Assert.Equal("Usuń: czwartek, 24 grudnia 2026", page.One("button[value='remove-exception:0']")!.GetAttribute("aria-label"));
        Assert.Equal("date", page.One("#new-exception")!.GetAttribute("type"));
        Assert.Equal("Dodaj dzień wolny", page.One("button[value=add-exception]")!.TextContent);
    }

    [Fact]
    public async Task The_save_bar_is_a_sticky_last_child_of_the_form_with_one_save_button_and_the_unsaved_sentence()
    {
        var (web, browser, _) = await PractitionerBrowsers.SignedIn(w => w.HaveProfile());
        await using var _ = web;
        var page = await browser.GetAsync("/gabinet-programisty/dostepnosc");
        var bar = page.One("form > [data-testid=save-bar]")!;
        Assert.Same(page.One("form")!.LastElementChild, bar);
        Assert.Contains("Zmiany w liście godzin są zapisane dopiero po kliknięciu „Zapisz”.", bar.TextContent);
        Assert.Equal(["Zapisz"], bar.QuerySelectorAll("button").Select(b => b.TextContent));
        Assert.Single(page.All("button"), b => b.TextContent.Contains("Zapisz") && b.GetAttribute("aria-hidden") != "true");
        var css = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Gabinety.Web", "Components", "Pages", "PractitionerArea", "Availability.razor.css"));
        Assert.Contains("position: sticky", css);
        Assert.Contains("bottom: 0", css);
    }

    [Fact]
    public async Task Add_for_a_day_and_copy_to_weekdays_work_on_the_posted_draft_and_keep_the_posted_shape()
    {
        var (web, browser, world) = await PractitionerBrowsers.SignedIn(w => w.HaveProfile());
        await using var _ = web;
        var added = await Post(browser, ("cmd", "add:3"), ("rule-0-day", "1"), ("rule-0-from", "09:00"), ("rule-0-to", "12:00"));
        Assert.Equal("3", added.One("input[name=rule-1-day]")!.GetAttribute("value"));
        Assert.Single(added.All("[data-testid=day]")[2].QuerySelectorAll("[data-testid=rule]"));
        var copied = await Post(browser, ("cmd", "copy:1"), ("rule-0-day", "1"), ("rule-0-from", "09:00"), ("rule-0-to", "12:00"), ("rule-1-day", "6"), ("rule-1-from", "10:00"), ("rule-1-to", "11:00"));
        Assert.Equal([1, 1, 1, 1, 1, 1, 0], copied.All("[data-testid=day]").Select(d => d.QuerySelectorAll("[data-testid=rule]").Length));
        Assert.Equal(6, copied.All("[data-testid=rule]").Count);
        Assert.DoesNotContain(copied.All("select"), s => s.GetAttribute("name")!.EndsWith("-day"));
        Assert.DoesNotContain(web.Booking.Requests, r => r.Method == HttpMethod.Put);
        Assert.Empty(world.Rules);
    }

    [Fact]
    public async Task With_no_windows_the_page_says_clients_see_no_times()
    {
        var (web, browser, _) = await PractitionerBrowsers.SignedIn(w => w.HaveProfile());
        await using var _ = web;
        var page = await browser.GetAsync("/gabinet-programisty/dostepnosc");
        Assert.Equal("Brak godzin: klienci nie zobaczą żadnych terminów.", page.TextOf("[data-testid=no-rules]"));
    }

    [Fact]
    public async Task The_commands_change_the_draft_without_saving_and_the_draft_travels_in_the_form()
    {
        var (web, browser, world) = await PractitionerBrowsers.SignedIn(w => { w.HaveProfile(); w.Rules.Add((1, "09:00", "12:00")); });
        await using var _ = web;
        var added = await Post(browser, ("cmd", "add"), ("rule-0-day", "1"), ("rule-0-from", "09:00"), ("rule-0-to", "12:00"));
        Assert.Equal(HttpStatusCode.OK, added.Status);
        Assert.Equal(2, added.All("[data-testid=rule]").Count);
        Assert.Equal("09:00", added.One("select[name=rule-1-from] option[selected]")!.TextContent); // the new row's default
        var removed = await Post(browser, ("cmd", "remove:0"), ("rule-0-day", "1"), ("rule-0-from", "09:00"), ("rule-0-to", "12:00"), ("rule-1-day", "2"), ("rule-1-from", "13:00"), ("rule-1-to", "14:00"));
        Assert.Single(removed.All("[data-testid=rule]"));
        Assert.Equal("2", removed.One("input[name=rule-0-day]")!.GetAttribute("value"));
        var exception = await Post(browser, ("cmd", "add-exception"), ("new-exception", "2027-02-03"), ("rule-0-day", "1"), ("rule-0-from", "09:00"), ("rule-0-to", "12:00"));
        Assert.Equal("środa, 3 lutego 2027", exception.TextOf("[data-testid=exception]"));
        Assert.DoesNotContain(web.Booking.Requests, r => r.Method == HttpMethod.Put); // none of them saved anything
        Assert.Equal([(1, "09:00", "12:00")], world.Rules);
    }

    [Fact]
    public async Task Saving_sends_the_whole_draft_with_the_bearer_and_redirects_with_the_flash_zapisano()
    {
        var (web, browser, world) = await PractitionerBrowsers.SignedIn(w => w.HaveProfile());
        await using var _ = web;
        var response = await Post(browser, ("cmd", "save"), ("rule-0-day", "5"), ("rule-0-from", "08:30"), ("rule-0-to", "10:00"), ("rule-1-day", "5"), ("rule-1-from", "10:00"), ("rule-1-to", "12:00"), ("exception-0", "2027-01-06"));
        Assert.Equal("/gabinet-programisty/dostepnosc?flash=saved", response.Location);
        var put = web.Booking.Requests.Single(r => r.Method == HttpMethod.Put);
        Assert.StartsWith("Bearer ", put.Authorization);
        var body = JsonNode.Parse(put.Body!)!;
        Assert.Equal(("08:30", "10:00", 5), (body["rules"]![0]!["startLocal"]!.GetValue<string>(), body["rules"]![0]!["endLocal"]!.GetValue<string>(), body["rules"]![0]!["isoWeekday"]!.GetValue<int>()));
        Assert.Equal("2027-01-06", body["exceptions"]![0]!["dateLocal"]!.GetValue<string>());
        Assert.Equal(2, world.Rules.Count);
        var after = await browser.GetAsync(response.Location!);
        Assert.Equal("Zapisano.", after.TextOf("[data-testid=flash]"));
    }

    [Fact]
    public async Task A_draft_with_problems_is_not_sent_and_every_problem_is_listed_with_its_row()
    {
        var (web, browser, _) = await PractitionerBrowsers.SignedIn(w => w.HaveProfile());
        await using var _ = web;
        var response = await Post(browser, ("cmd", "save"), ("rule-0-day", "1"), ("rule-0-from", "10:00"), ("rule-0-to", "09:00"), ("rule-1-day", "2"), ("rule-1-from", "09:00"), ("rule-1-to", "11:00"), ("rule-2-day", "2"), ("rule-2-from", "10:30"), ("rule-2-to", "12:00"), ("exception-0", "2027-02-30"));
        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.DoesNotContain(web.Booking.Requests, r => r.Method == HttpMethod.Put);
        var box = response.One("[data-testid=availability-issues]")!;
        Assert.Equal("alert", box.GetAttribute("role"));
        var items = box.QuerySelectorAll("li").Select(l => l.TextContent).ToList();
        Assert.Contains("Przedział 1: Koniec musi być po początku.", items);
        Assert.Contains("Przedziały w dniu „wtorek” nakładają się. Popraw je przed zapisem.", items);
        Assert.Contains("Wpisz prawdziwą datę dnia wolnego.", items);
        Assert.Equal("true", response.All("[data-testid=rule]")[0].QuerySelector("select")!.GetAttribute("aria-invalid"));
        Assert.Equal(3, response.All("[data-testid=rule]").Count); // the draft is still there to fix
    }

    [Fact]
    public async Task A_refusal_of_the_service_is_shown_as_a_row_note_without_any_text_of_the_server()
    {
        var (web, browser, _) = await PractitionerBrowsers.SignedIn(w => w.HaveProfile());
        await using var _ = web;
        web.Booking.Respond = r => r.Method == HttpMethod.Put ? new HttpResponseMessage(HttpStatusCode.UnprocessableEntity) { Content = new StringContent("""{"code":"validation_failed","title":"SERVER-TITLE","errors":{"rules[0].endLocal":["SERVER-MESSAGE"]}}""", System.Text.Encoding.UTF8, "application/problem+json") } : FakeBackend.Json(new { rules = Array.Empty<object>(), exceptions = Array.Empty<object>() });
        var response = await Post(browser, ("cmd", "save"), ("rule-0-day", "1"), ("rule-0-from", "09:00"), ("rule-0-to", "10:00"));
        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Contains("Przedział 1: sprawdź dzień i godziny", response.TextOf("[data-testid=availability-issues]"));
        Assert.DoesNotContain("SERVER-", response.Html);
    }

    [Fact]
    public async Task A_booking_service_that_is_down_is_a_503_with_a_retry_and_an_account_that_is_not_a_practitioner_goes_to_the_dashboard()
    {
        var (web, browser, world) = await PractitionerBrowsers.SignedIn(w => { w.HaveProfile(); w.BookingDown = true; });
        await using var _ = web;
        var down = await browser.GetAsync("/gabinet-programisty/dostepnosc");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, down.Status);
        Assert.Equal("/gabinet-programisty/dostepnosc", down.One("[data-testid=unavailable] a")!.GetAttribute("href"));
        world.BookingDown = false;
        web.Booking.Respond = _ => new HttpResponseMessage(HttpStatusCode.Forbidden); // Directory has a profile, Booking does not know the practitioner (yet)
        var forbidden = await browser.GetAsync("/gabinet-programisty/dostepnosc");
        Assert.Equal("/gabinet-programisty", forbidden.Location);
    }

    [Fact]
    public async Task Hostile_commands_and_a_long_form_do_nothing_bad()
    {
        var (web, browser, _) = await PractitionerBrowsers.SignedIn(w => w.HaveProfile());
        await using var _ = web;
        var junk = await Post(browser, ("cmd", "remove:-5"), ("rule-0-day", "x"), ("rule-0-from", "99"), ("rule-0-to", ""));
        Assert.Equal(HttpStatusCode.OK, junk.Status);
        var weird = await Post(browser, ("cmd", "remove:99999999999999999999"));
        Assert.Equal(HttpStatusCode.OK, weird.Status);
        var many = Enumerable.Range(0, 40).SelectMany(i => new[] { ($"rule-{i}-day", "1"), ($"rule-{i}-from", "09:00"), ($"rule-{i}-to", "10:00") }).Append(("cmd", "save")).ToArray();
        var big = await Post(browser, many);
        Assert.Equal(HttpStatusCode.OK, big.Status);
        Assert.Contains("najwyżej 28", big.TextOf("[data-testid=availability-issues]"));
        Assert.DoesNotContain(web.Booking.Requests, r => r.Method == HttpMethod.Put);
        Assert.True(big.All("[data-testid=rule]").Count <= 29);
    }
}
