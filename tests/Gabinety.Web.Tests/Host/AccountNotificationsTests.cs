using System.Net;
using System.Text.Json;
using Gabinety.Web.Api;
using Gabinety.Web.Components.Pages.Account;

namespace Gabinety.Web.Tests.Pipeline;

/// <summary>
/// The notification section of <c>/konto</c> (T-11.13) against the real host with the fake Booking backend: the three switches, the save that round-trips, and the section failing soft
/// while the rest of the page keeps working.
/// </summary>
public class AccountNotificationsTests
{
    const string Path = "/api/v1/me/notification-preferences";

    static async Task<(WebFactory Web, Browser Browser)> SignedIn()
    {
        var web = new WebFactory();
        var browser = web.NewBrowser();
        await SignIn.As(web, browser, returnTo: "/konto");
        return (web, browser);
    }

    /// <summary>A Booking that keeps the switches in memory: a PUT sets the kinds it names, a GET and the answer of a PUT list all three.</summary>
    static Dictionary<string, bool> StoreOf(WebFactory web, bool reminder24h = true, bool reminder1h = true, bool reviewRequest = true)
    {
        var stored = new Dictionary<string, bool> { ["Reminder24h"] = reminder24h, ["Reminder1h"] = reminder1h, ["ReviewRequest"] = reviewRequest };
        web.Booking.Respond = r =>
        {
            if (r.RequestUri!.PathAndQuery != Path) return new HttpResponseMessage(HttpStatusCode.NotFound);
            if (r.Method == HttpMethod.Put)
            {
                using var sent = JsonDocument.Parse(web.Booking.Requests[^1].Body!); // the fake records the call before it answers
                foreach (var item in sent.RootElement.GetProperty("items").EnumerateArray())
                    stored[item.GetProperty("kind").GetString()!] = item.GetProperty("enabled").GetBoolean();
            }
            return FakeBackend.Json(new { items = stored.Select(kv => new { kind = kv.Key, enabled = kv.Value }) });
        };
        return stored;
    }

    static bool Checked(Page page, string name) => page.One($"input[name='Notifications.{name}']")!.HasAttribute("checked");

    static Dictionary<string, string> Save(params string[] ticked)
    {
        var f = new Dictionary<string, string>();
        foreach (var name in ticked) f[$"Notifications.{name}"] = "true"; // an unticked box is not posted at all
        return f;
    }

    [Fact]
    public async Task The_section_has_three_labelled_switches_and_says_the_transactional_messages_are_always_sent()
    {
        var (web, browser) = await SignedIn();
        await using var __ = web;
        StoreOf(web);
        var page = await browser.GetAsync("/konto");
        var section = page.One("section[aria-labelledby=account-notifications]")!;
        Assert.Equal("Powiadomienia e-mail", section.QuerySelector("h2")!.TextContent);
        Assert.Contains("Potwierdzenia rezerwacji, anulowania, spory i wypłaty wysyłamy zawsze.", section.TextContent);
        Assert.Equal(3, section.QuerySelectorAll("input[type=checkbox]").Length);
        Assert.Contains("Przypomnienie 24 godziny przed", section.QuerySelector("label:nth-of-type(1)")!.TextContent);
        Assert.Contains("Przypomnienie godzinę przed", section.QuerySelector("label:nth-of-type(2)")!.TextContent);
        Assert.Contains("Prośba o opinię po konsultacji", section.QuerySelector("label:nth-of-type(3)")!.TextContent);
        Assert.True(Checked(page, "Reminder24h") && Checked(page, "Reminder1h") && Checked(page, "ReviewRequest")); // all three on, as the service lists them
        Assert.Equal(0, section.QuerySelectorAll("script").Length); // static: no script
    }

    [Fact]
    public async Task The_switches_show_what_the_service_stores_and_a_save_sends_all_three_and_shows_what_is_stored()
    {
        var (web, browser) = await SignedIn();
        await using var __ = web;
        var stored = StoreOf(web);
        Assert.NotNull((await browser.GetAsync("/konto")).One("input[name='Notifications.Reminder24h']"));

        stored["Reminder1h"] = false; // the service holds a switch off that the page did not set
        var before = await browser.GetAsync("/konto");
        Assert.False(Checked(before, "Reminder1h"));
        Assert.True(Checked(before, "Reminder24h"));

        // the user unticks the 24 h reminder and ticks the review request, then saves
        var response = await browser.PostFormAsync("/konto", NotificationPreferencesForm.FormName, Save("Reminder1h", "ReviewRequest"));
        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal("Zapisano.", response.TextOf("[data-testid=notifications-saved]"));
        Assert.Null(response.One("[data-testid=notifications-error]"));
        Assert.False(Checked(response, "Reminder24h"));
        Assert.True(Checked(response, "Reminder1h"));
        Assert.True(Checked(response, "ReviewRequest"));

        var put = web.Booking.Requests.Last(r => r.Method == HttpMethod.Put && r.PathAndQuery == Path);
        Assert.StartsWith("Bearer eyJ", put.Authorization);
        using var sent = JsonDocument.Parse(put.Body!);
        var items = sent.RootElement.GetProperty("items").EnumerateArray().Select(i => (Kind: i.GetProperty("kind").GetString(), Enabled: i.GetProperty("enabled").GetBoolean())).ToList();
        Assert.Equal([("Reminder24h", false), ("Reminder1h", true), ("ReviewRequest", true)], items); // every switch named, nothing else

        Assert.False(stored["Reminder24h"]); // the service holds the save
        var next = await browser.GetAsync("/konto");
        Assert.False(Checked(next, "Reminder24h"));
        Assert.True(Checked(next, "Reminder1h"));
        Assert.Null(next.One("[data-testid=notifications-saved]")); // the flash is for the save only
    }

    [Fact]
    public async Task When_the_switches_cannot_be_read_only_their_callout_shows_and_the_other_sections_work()
    {
        var (web, browser) = await SignedIn();
        await using var __ = web;
        web.Booking.Respond = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        var page = await browser.GetAsync("/konto");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal("Ustawienia powiadomień są chwilowo niedostępne. Spróbuj ponownie za chwilę.", page.TextOf("[data-testid=notifications-unavailable]"));
        Assert.Equal("alert", page.One("[data-testid=notifications-unavailable]")!.GetAttribute("role"));
        Assert.Null(page.One("input[name='Notifications.Reminder24h']")); // no switch shown without the service's state
        Assert.Equal("Konto", page.TextOf("h1"));
        Assert.NotNull(page.One("[data-testid=consents]"));
        Assert.NotNull(page.One("#account-password"));
        Assert.NotNull(page.One("details.account__danger"));
        Assert.Null(page.One("[data-testid=unavailable]")); // the account data itself is fine
    }

    [Fact]
    public async Task A_refused_save_keeps_the_ticks_the_user_made_and_says_so_and_the_other_sections_stay()
    {
        var (web, browser) = await SignedIn();
        await using var __ = web;
        StoreOf(web);
        var inner = web.Booking.Respond;
        web.Booking.Respond = r => r.Method == HttpMethod.Put ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : inner(r);

        var response = await browser.PostFormAsync("/konto", NotificationPreferencesForm.FormName, Save("Reminder1h"));
        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal("Nie udało się zapisać ustawień powiadomień. Spróbuj ponownie za chwilę.", response.TextOf("[data-testid=notifications-error]"));
        Assert.Null(response.One("[data-testid=notifications-saved]"));
        Assert.False(Checked(response, "Reminder24h")); // the choice the user made is still on the page
        Assert.True(Checked(response, "Reminder1h"));
        Assert.NotNull(response.One("#account-password"));
        Assert.NotNull(response.One("details.account__danger"));
    }

    [Fact]
    public async Task A_save_of_a_page_loaded_before_the_switches_went_down_still_reaches_the_service_and_its_refusal_is_said()
    {
        var (web, browser) = await SignedIn();
        await using var __ = web;
        StoreOf(web);
        var loaded = await browser.GetAsync("/konto"); // the page with its form and antiforgery token, while Booking still answers
        web.Booking.Respond = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        var form = loaded.One("section[aria-labelledby=account-notifications] form")!;
        var fields = form.QuerySelectorAll("input[type=hidden]").Select(i => KeyValuePair.Create(i.GetAttribute("name")!, i.GetAttribute("value") ?? ""))
            .Concat(Save("Reminder24h", "Reminder1h", "ReviewRequest")).ToList();

        var response = await browser.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/konto") { Content = new FormUrlEncodedContent(fields) });

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal("Nie udało się zapisać ustawień powiadomień. Spróbuj ponownie za chwilę.", response.TextOf("[data-testid=notifications-error]"));
        Assert.Null(response.One("[data-testid=notifications-unavailable]"));
        Assert.Contains(web.Booking.Requests, r => r.Method == HttpMethod.Put && r.PathAndQuery == Path);
    }

    [Fact]
    public async Task The_form_is_posted_with_antiforgery_to_the_account_page_and_is_not_on_any_other_page()
    {
        var (web, browser) = await SignedIn();
        await using var __ = web;
        StoreOf(web);
        var page = await browser.GetAsync("/konto");
        var form = page.One("section[aria-labelledby=account-notifications] form[method=post]")!;
        Assert.Equal("/konto", form.GetAttribute("action") ?? "/konto");
        Assert.NotNull(form.QuerySelector("input[name='__RequestVerificationToken']"));
        Assert.NotNull(form.QuerySelector("input[name='_handler'][value='notification-preferences']"));
        Assert.Single(page.All("section[aria-labelledby=account-notifications] form"));
    }
}
