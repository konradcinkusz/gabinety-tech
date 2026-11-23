using System.Net;
using System.Text.Json;
using Gabinety.Web.Api;
using Gabinety.Web.Tests.PractitionerAreaTests;

namespace Gabinety.Web.Tests.Pages;

/// <summary>The lists of proposals, for the client (<c>/moje/propozycje</c>) and the programmer (<c>/gabinet-programisty/propozycje</c>) (ADR proposal 0013).</summary>
public class ProposalListPageTests
{
    static Func<HttpRequestMessage, HttpResponseMessage> List(PageDto<ProposalSummaryDto> page) => r =>
        r.Method == HttpMethod.Get && r.RequestUri!.AbsolutePath == "/api/v1/me/proposals" ? FakeBackend.Json(page) : new HttpResponseMessage(HttpStatusCode.NotFound);

    [Fact]
    public async Task The_clients_list_has_a_row_per_proposal_with_the_counterpart_the_state_and_whose_turn_it_is()
    {
        await using var f = await P.Open(List(new PageDto<ProposalSummaryDto>([P.Row("AwaitingClient", yourTurn: true), P.Row("AwaitingPractitioner", id: Guid.NewGuid()), P.Row("Accepted", id: Guid.NewGuid())], null)));
        var page = await f.Browser.GetAsync("/moje/propozycje");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal("Moje propozycje", page.TextOf("h1"));
        Assert.Equal("noindex,follow", page.One("meta[name=robots]")!.GetAttribute("content"));
        var rows = page.All("[data-testid=proposal-row]");
        Assert.Equal(3, rows.Count);
        Assert.Equal("Anna Demowska", rows[0].QuerySelector("h2")!.TextContent); // the client sees the programmer
        Assert.Equal(["Czeka na klienta", "Czeka na programistę", "Zaakceptowana"], rows.Select(r => r.QuerySelector(".badge")!.TextContent));
        Assert.Equal(["badge badge--warning", "badge badge--warning", "badge badge--success"], rows.Select(r => r.QuerySelector(".badge")!.ClassName));
        Assert.Equal("Twoja kolej", rows[0].QuerySelector("[data-testid=your-turn]")!.TextContent);
        Assert.Contains("Czekasz na odpowiedź", rows[1].TextContent);
        Assert.Null(rows[2].QuerySelector("[data-testid=your-turn]")); // a closed proposal has no turn
        Assert.Equal($"/moje/propozycje/{P.Id}", rows[0].QuerySelector("a.proposal-row__link")!.GetAttribute("href"));
        Assert.Contains("Szczegóły", rows[0].QuerySelector("a.proposal-row__link")!.TextContent);
        Assert.DoesNotContain("Szukamy", page.Html); // the message is never in a list
        Assert.Equal("client", ((string)f.Web.Booking.Requests.Single().PathAndQuery).Split("role=")[1]);
    }

    [Fact]
    public async Task The_tabs_filter_by_state_with_plain_links_and_the_current_one_is_marked()
    {
        await using var f = await P.Open(List(new PageDto<ProposalSummaryDto>([P.Row()], null)));
        var all = await f.Browser.GetAsync("/moje/propozycje");
        Assert.Equal(["/moje/propozycje", "/moje/propozycje?state=open", "/moje/propozycje?state=closed"], all.All("nav.tabs a").Select(a => a.GetAttribute("href")));
        Assert.Equal(["Wszystkie", "Otwarte", "Zamknięte"], all.All("nav.tabs a").Select(a => a.TextContent));
        Assert.Equal(["Wszystkie"], all.All("nav.tabs a[aria-current=page]").Select(a => a.TextContent));

        var open = await f.Browser.GetAsync("/moje/propozycje?state=open");
        Assert.Equal(["Otwarte"], open.All("nav.tabs a[aria-current=page]").Select(a => a.TextContent));
        Assert.EndsWith("role=client&state=open", f.Web.Booking.Requests.Last().PathAndQuery);

        await f.Browser.GetAsync("/moje/propozycje?state=garbage"); // not a state: every state, never an error
        Assert.EndsWith("role=client", f.Web.Booking.Requests.Last().PathAndQuery);
    }

    [Fact]
    public async Task More_is_a_link_with_the_cursor_that_keeps_the_state()
    {
        await using var f = await P.Open(List(new PageDto<ProposalSummaryDto>([P.Row()], "C 1+/")));
        var page = await f.Browser.GetAsync("/moje/propozycje?state=closed");
        var next = page.One("a[rel=next]")!;
        Assert.Equal("/moje/propozycje?state=closed&cursor=C%201%2B%2F", next.GetAttribute("href"));
        await f.Browser.GetAsync(next.GetAttribute("href")!);
        Assert.EndsWith("role=client&state=closed&cursor=C%201%2B%2F", f.Web.Booking.Requests.Last().PathAndQuery);
    }

    [Fact]
    public async Task An_empty_list_says_what_to_do_and_a_filtered_one_offers_all_states()
    {
        await using var f = await P.Open(List(new PageDto<ProposalSummaryDto>([], null)));
        var empty = await f.Browser.GetAsync("/moje/propozycje");
        Assert.Equal("Brak propozycji", empty.TextOf("[data-testid=proposals-empty] h2"));
        Assert.Contains("Gdy wyślesz propozycję", empty.TextOf("[data-testid=proposals-empty] p"));
        Assert.Equal("/programisci", empty.One("[data-testid=proposals-empty] a.btn--primary")!.GetAttribute("href"));
        var filtered = await f.Browser.GetAsync("/moje/propozycje?state=open");
        Assert.Equal("W tym stanie nie ma żadnych propozycji.", filtered.TextOf("[data-testid=proposals-empty] p"));
        Assert.Equal("/moje/propozycje", filtered.One("[data-testid=proposals-empty] a")!.GetAttribute("href"));
    }

    [Fact]
    public async Task A_service_that_is_down_is_an_alert_with_retry_and_a_dead_session_says_so()
    {
        await using var f = await P.Open(FakeBackend.Throw(new HttpRequestException("down")));
        var down = await f.Browser.GetAsync("/moje/propozycje?state=open");
        Assert.Equal("alert", down.One("[data-testid=unavailable]")!.GetAttribute("role"));
        Assert.Equal("/moje/propozycje?state=open", down.One("[data-testid=unavailable] a")!.GetAttribute("href"));
        f.Web.Booking.Respond = _ => FakeBackend.Problem(HttpStatusCode.Unauthorized, "unauthenticated");
        var out_ = await f.Browser.GetAsync("/moje/propozycje");
        Assert.Equal("alert", out_.One("[data-testid=proposals-signed-out]")!.GetAttribute("role"));
        Assert.StartsWith("/logowanie?redirect=", out_.One("[data-testid=proposals-signed-out] a")!.GetAttribute("href"));
    }

    [Fact]
    public async Task A_visitor_is_sent_to_sign_in()
    {
        await using var web = new WebFactory();
        var page = await web.NewBrowser().GetAsync("/moje/propozycje");
        Assert.Equal(HttpStatusCode.Found, page.Status);
        Assert.StartsWith("/logowanie?redirect=", page.Location);
    }

    [Fact]
    public async Task The_programmers_inbox_is_in_the_practitioner_area_with_the_clients_and_their_company()
    {
        await using var f = await P.Open(List(new PageDto<ProposalSummaryDto>([P.Row("AwaitingPractitioner", yourTurn: true, client: "Klient J.", company: "Demo Sp. z o.o.")], null)));
        var page = await f.Browser.GetAsync("/gabinet-programisty/propozycje");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal("Propozycje współpracy", page.TextOf("h1"));
        Assert.Equal("noindex,nofollow", page.One("meta[name=robots]")!.GetAttribute("content"));
        Assert.Equal(["Propozycje"], page.One("nav[aria-label='Sekcje panelu programisty']")!.QuerySelectorAll("a[aria-current=page]").Select(a => a.TextContent));
        var row = page.One("[data-testid=proposal-row]")!;
        Assert.Equal("Klient J. (Demo Sp. z o.o.)", row.QuerySelector("h2")!.TextContent);
        Assert.Equal("Twoja kolej", row.QuerySelector("[data-testid=your-turn]")!.TextContent);
        Assert.Equal($"/gabinet-programisty/propozycje/{P.Id}", row.QuerySelector("a.proposal-row__link")!.GetAttribute("href"));
        Assert.EndsWith("role=practitioner", f.Web.Booking.Requests.Single().PathAndQuery);
        Assert.Equal(["/gabinet-programisty/propozycje", "/gabinet-programisty/propozycje?state=open", "/gabinet-programisty/propozycje?state=closed"], page.All("nav.tabs[aria-label='Stan propozycji'] a").Select(a => a.GetAttribute("href")));
    }

    [Fact]
    public async Task An_empty_inbox_points_to_the_availability_setting()
    {
        await using var f = await P.Open(List(new PageDto<ProposalSummaryDto>([], null)));
        var page = await f.Browser.GetAsync("/gabinet-programisty/propozycje");
        Assert.Contains("Gdy klient wyśle Ci propozycję", page.TextOf("[data-testid=proposals-empty] p"));
        Assert.Equal("/gabinet-programisty/profil", page.One("[data-testid=proposals-empty] a")!.GetAttribute("href"));
    }
}

/// <summary>One proposal: what each party sees and every action (ADR proposal 0013).</summary>
public class ProposalDetailPageTests
{
    static string ClientPath => $"/moje/propozycje/{P.Id}";
    static string PractitionerPath => $"/gabinet-programisty/propozycje/{P.Id}";

    static KeyValuePair<string, string> F(string name, string value) => KeyValuePair.Create(name, value);

    static string Dd(Page page) => string.Join(" | ", page.All("[data-testid=proposal-terms] dd").Select(d => Page.Squash(d.TextContent)));

    // ---- what is shown

    [Fact]
    public async Task The_terms_the_state_and_the_rounds_are_shown_as_text()
    {
        var rounds = new[]
        {
            new ProposalRoundDto(1, "Client", "Proposed", P.Terms, "Szukamy <b>programisty</b> do współpracy.", P.Now.AddDays(-1)),
            new ProposalRoundDto(2, "Practitioner", "Countered", P.Terms with { Rate = new MoneyDto(18000, "PLN") }, null, P.Now.AddHours(-2)),
        };
        await using var f = await P.Open(P.Booking(P.View("AwaitingClient", "Client", accept: true, counter: true, reject: true, withdraw: true, company: "Demo Sp. z o.o.", round: 2, rounds: rounds)));
        var page = await f.Browser.GetAsync(ClientPath);
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal("Propozycja współpracy", page.TextOf("h1"));
        Assert.Equal("Współpraca długoterminowa", page.TextOf("#proposal-title"));
        Assert.Equal("badge badge--warning", page.One("section.card .badge")!.ClassName);
        Assert.Equal("Czeka na klienta", page.One("section.card .badge")!.TextContent);
        Assert.Equal(["Programista", "Klient", "Firma", "Stawka", "Wymiar", "Okres", "Start", "Wygasa "], page.All("[data-testid=proposal-terms] dt").Select(d => d.TextContent));
        Assert.Contains("Anna Demowska | Klient J. | Demo Sp. z o.o.", Dd(page));
        Assert.Contains("20 godz. tygodniowo | 6 mies.", Dd(page));
        Assert.Equal("To Twoja kolej: przyjmij warunki, zaproponuj inne albo odmów.", page.TextOf("section.card p strong"));
        var thread = page.All("[data-testid=proposal-round]");
        Assert.Equal(["Runda 1: Propozycja (klient)", "Runda 2: Kontrpropozycja (programista)"], thread.Select(t => t.QuerySelector("h3")!.TextContent));
        Assert.Equal("Szukamy <b>programisty</b> do współpracy.", thread[0].QuerySelector(".proposal-thread__text")!.TextContent); // text, not markup
        Assert.Empty(thread[0].QuerySelectorAll("b"));
        Assert.Null(thread[1].QuerySelector(".proposal-thread__text")); // a round without a message
        Assert.Equal("/moje/propozycje", page.One("p > a[href='/moje/propozycje']")!.GetAttribute("href"));
        Assert.Empty(page.All("[data-testid=proposal-contact]")); // not accepted: no address
    }

    [Theory]
    [InlineData(true, true, true, false, new[] { "Przyjmij warunki", "Zaproponuj inne warunki", "Odmów" })]
    [InlineData(false, false, false, true, new[] { "Wycofaj propozycję" })]
    [InlineData(true, false, true, true, new[] { "Przyjmij warunki", "Odmów", "Wycofaj propozycję" })] // the counter cap: only accept, reject (and withdraw for the client)
    [InlineData(true, true, true, true, new[] { "Przyjmij warunki", "Zaproponuj inne warunki", "Odmów", "Wycofaj propozycję" })]
    public async Task The_buttons_are_exactly_what_the_server_flags_allow(bool accept, bool counter, bool reject, bool withdraw, string[] labels)
    {
        await using var f = await P.Open(P.Booking(P.View("AwaitingClient", accept: accept, counter: counter, reject: reject, withdraw: withdraw)));
        var page = await f.Browser.GetAsync(ClientPath);
        Assert.Equal(labels, page.All("form[method=get] button").Select(b => b.TextContent));
        Assert.All(page.All("form[method=get] button"), b => Assert.Equal("action", b.GetAttribute("name")));
        Assert.Equal(ClientPath, page.One("form[method=get]")!.GetAttribute("action"));
        Assert.Equal(accept ? 1 : 0, page.All("form[method=get] button.btn--primary").Count); // one main action at most
        Assert.Equal((accept || reject) && !counter, page.All("p.muted").Any(p => p.TextContent.Contains("limit kontrpropozycji"))); // said when only the counter is missing
    }

    [Fact]
    public async Task A_closed_proposal_has_no_buttons_and_says_so_and_no_deadline()
    {
        await using var f = await P.Open(P.Booking(P.View("Rejected")));
        var page = await f.Browser.GetAsync(ClientPath);
        Assert.Empty(page.All("form[method=get]"));
        Assert.Equal("Ta propozycja jest zamknięta.", page.TextOf("section.card p.muted"));
        Assert.DoesNotContain("Wygasa", page.All("[data-testid=proposal-terms] dt").Select(d => d.TextContent));
        Assert.Equal("badge badge--danger", page.One("section.card .badge")!.ClassName);
    }

    [Fact]
    public async Task The_address_of_the_other_party_is_shown_only_once_accepted_and_only_as_a_plain_address()
    {
        await using var f = await P.Open(P.Booking(P.View("Accepted", contact: true, email: "anna@demo.example.com")));
        var page = await f.Browser.GetAsync(ClientPath);
        var contact = page.One("[data-testid=proposal-contact]")!;
        Assert.Equal("Dane kontaktowe", contact.QuerySelector("h2")!.TextContent);
        Assert.Equal(("anna@demo.example.com", "mailto:anna@demo.example.com"), (contact.QuerySelector("[data-testid=contact-email]")!.TextContent, contact.QuerySelector("[data-testid=contact-email]")!.GetAttribute("href")));

        await using var g = await P.Open(P.Booking(P.View("Accepted", contact: true, email: null)));
        var missing = await g.Browser.GetAsync(ClientPath);
        Assert.Contains("niedostępny", missing.TextOf("[data-testid=contact-missing]"));
        Assert.Null(missing.One("[data-testid=contact-email]"));

        await using var h = await P.Open(P.Booking(P.View("Accepted", contact: true, email: "a b\"@x.test")));
        Assert.Null((await h.Browser.GetAsync(ClientPath)).One("[data-testid=contact-email]")); // an address that could leave the attribute is never a link
    }

    [Fact]
    public async Task The_flash_is_a_fixed_text_by_key_and_an_unknown_key_shows_nothing()
    {
        await using var f = await P.Open(P.Booking(P.View()));
        var texts = new Dictionary<string, string>
        {
            ["sent"] = "Propozycja została wysłana. Programista dostanie powiadomienie.", ["accepted"] = "Warunki zostały przyjęte. Adres drugiej strony widzisz poniżej.",
            ["countered"] = "Kontrpropozycja została wysłana.", ["rejected"] = "Propozycja została odrzucona.", ["withdrawn"] = "Propozycja została wycofana.",
        };
        foreach (var (key, text) in texts) Assert.Equal(text, (await f.Browser.GetAsync(ClientPath + "?done=" + key)).TextOf("[data-testid=done]"));
        Assert.Null((await f.Browser.GetAsync(ClientPath + "?done=<script>")).One("[data-testid=done]"));
        Assert.Equal("status", (await f.Browser.GetAsync(ClientPath + "?done=sent")).One("[data-testid=done]")!.GetAttribute("role"));
    }

    // ---- who is who

    [Fact]
    public async Task The_other_party_is_sent_to_their_own_page_and_a_stranger_gets_the_answer_of_an_unknown_id()
    {
        await using var f = await P.Open(P.Booking(P.View(role: "Practitioner")));
        var wrong = await f.Browser.GetAsync(ClientPath);
        Assert.Equal(HttpStatusCode.Found, wrong.Status);
        Assert.Equal(PractitionerPath, wrong.Location);

        f.Web.Booking.Respond = P.Booking(P.View(role: "Client"));
        var back = await f.Browser.GetAsync(PractitionerPath);
        Assert.Equal(ClientPath, back.Location);

        f.Web.Booking.Respond = _ => FakeBackend.Problem(HttpStatusCode.NotFound, "not_found");
        Assert.Equal(HttpStatusCode.NotFound, (await f.Browser.GetAsync(ClientPath)).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await f.Browser.GetAsync(PractitionerPath)).Status);
    }

    [Fact]
    public async Task A_dead_session_goes_to_sign_in_and_a_service_that_is_down_is_an_alert()
    {
        await using var f = await P.Open(_ => FakeBackend.Problem(HttpStatusCode.Unauthorized, "unauthenticated"));
        var out_ = await f.Browser.GetAsync(ClientPath);
        Assert.Equal(HttpStatusCode.Found, out_.Status);
        Assert.StartsWith("/logowanie?redirect=", out_.Location);
        f.Web.Booking.Respond = FakeBackend.Throw(new HttpRequestException("down"));
        var down = await f.Browser.GetAsync(ClientPath);
        Assert.Equal("alert", down.One("[data-testid=unavailable]")!.GetAttribute("role"));
        Assert.Equal(ClientPath, down.One("[data-testid=unavailable] a")!.GetAttribute("href"));
    }

    // ---- the actions

    [Fact]
    public async Task Accepting_asks_first_then_posts_the_round_the_user_saw_and_comes_back_with_a_fixed_key()
    {
        await using var f = await P.Open(P.Booking(P.View("AwaitingPractitioner", "Practitioner", accept: true, counter: true, reject: true, round: 3), r => FakeBackend.Json(P.View("Accepted", "Practitioner"))));
        var dialog = await f.Browser.GetAsync(PractitionerPath + "?action=accept");
        Assert.Equal("alertdialog", dialog.One("dialog[open]")!.GetAttribute("role"));
        Assert.Equal("Przyjąć te warunki?", dialog.TextOf("[data-testid=accept-dialog] h2"));
        Assert.Equal(PractitionerPath, dialog.One("[data-testid=accept-dialog] a.btn--secondary")!.GetAttribute("href"));
        Assert.Equal("3", dialog.One("[data-testid=accept-dialog] input[name=round]")!.GetAttribute("value"));
        Assert.DoesNotContain(f.Web.Booking.Requests, r => r.Method == HttpMethod.Post); // opening the dialog changes nothing

        var done = await f.Browser.Submit(PractitionerPath + "?action=accept", "accept");
        Assert.Equal(HttpStatusCode.Found, done.Status);
        Assert.Equal(PractitionerPath + "?done=accepted", done.Location);
        var post = f.Web.Booking.Requests.Single(r => r.Method == HttpMethod.Post);
        Assert.Equal($"/api/v1/proposals/{P.Id}/accept", post.PathAndQuery);
        Assert.Equal(3, JsonDocument.Parse(post.Body!).RootElement.GetProperty("round").GetInt32());
    }

    [Fact]
    public async Task Rejecting_and_withdrawing_confirm_in_a_dialog_and_post_to_their_own_routes()
    {
        await using var f = await P.Open(P.Booking(P.View("AwaitingClient", "Client", reject: true, withdraw: true, round: 2), _ => FakeBackend.Json(P.View("Rejected"))));
        Assert.Equal("Odmówić tej propozycji?", (await f.Browser.GetAsync(ClientPath + "?action=reject")).TextOf("[data-testid=reject-dialog] h2"));
        var rejected = await f.Browser.Submit(ClientPath + "?action=reject", "reject");
        Assert.Equal(ClientPath + "?done=rejected", rejected.Location);
        Assert.Equal(2, JsonDocument.Parse(f.Web.Booking.Requests.Last().Body!).RootElement.GetProperty("round").GetInt32());

        Assert.Equal("Wycofać propozycję?", (await f.Browser.GetAsync(ClientPath + "?action=withdraw")).TextOf("[data-testid=withdraw-dialog] h2"));
        var withdrawn = await f.Browser.Submit(ClientPath + "?action=withdraw", "withdraw");
        Assert.Equal(ClientPath + "?done=withdrawn", withdrawn.Location);
        Assert.Equal([$"/api/v1/proposals/{P.Id}/reject", $"/api/v1/proposals/{P.Id}/withdraw"], f.Web.Booking.Requests.Where(r => r.Method == HttpMethod.Post).Select(r => r.PathAndQuery));
    }

    [Fact]
    public async Task An_action_the_flags_do_not_allow_is_not_offered_even_when_the_url_asks_for_it()
    {
        await using var f = await P.Open(P.Booking(P.View("AwaitingPractitioner", "Client", withdraw: true)));
        foreach (var action in new[] { "accept", "reject", "counter" })
        {
            var page = await f.Browser.GetAsync(ClientPath + "?action=" + action);
            Assert.Empty(page.All("dialog")); Assert.Empty(page.All("[data-testid=counter-form]"));
            Assert.Equal(["Wycofaj propozycję"], page.All("form[method=get] button").Select(b => b.TextContent)); // the buttons of the state
        }
        Assert.Empty((await f.Browser.GetAsync(ClientPath + "?action=<x>")).All("dialog"));
    }

    [Fact]
    public async Task An_answer_to_terms_that_changed_says_so_and_offers_to_reload()
    {
        await using var f = await P.Open(P.Booking(P.View("AwaitingClient", accept: true, counter: true, reject: true), _ => FakeBackend.Problem(HttpStatusCode.Conflict, "invalid_transition")));
        var page = await f.Browser.Submit(ClientPath + "?action=accept", "accept");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal("Propozycja zmieniła się w międzyczasie. Odśwież stronę i sprawdź jej aktualny stan.", Page.Squash(page.TextOf("[data-testid=action-error] p")));
        Assert.Equal(ClientPath, page.One("[data-testid=action-error] a")!.GetAttribute("href"));
        Assert.Equal("alert", page.One("[data-testid=action-error]")!.GetAttribute("role"));
        Assert.DoesNotContain("SERVER-", page.Html);
    }

    // ---- the counter

    [Fact]
    public async Task The_counter_form_starts_from_the_current_terms_and_checks_everything_before_calling()
    {
        await using var f = await P.Open(P.Booking(P.View("AwaitingPractitioner", "Practitioner", accept: true, counter: true, reject: true, round: 1, terms: P.Terms with { Rate = new MoneyDto(15050, "PLN") })));
        var page = await f.Browser.GetAsync(PractitionerPath + "?action=counter");
        var form = page.One("[data-testid=counter-form]")!;
        Assert.Equal("Nowe warunki", form.QuerySelector("h2")!.TextContent);
        Assert.Equal(("150,50", "20", "6", P.StartIso), (page.One("#rate")!.GetAttribute("value")!, page.One("#hoursPerWeek")!.GetAttribute("value")!, page.One("#durationMonths")!.GetAttribute("value")!, page.One("#startDate")!.GetAttribute("value")!));
        Assert.Equal("Wyślij kontrpropozycję", form.QuerySelector("button[type=submit]")!.TextContent);
        Assert.Equal("Wiadomość do drugiej strony (opcjonalnie)", Page.Squash(form.QuerySelector("label[for=text]")!.TextContent));

        var bad = await f.Browser.Submit(PractitionerPath + "?action=counter", "counter", [F("rate", "1"), F("hoursPerWeek", "99"), F("startDate", "2020-01-01"), F("text", new string('x', 1001))]);
        Assert.Equal(HttpStatusCode.OK, bad.Status);
        Assert.Equal(["#rate", "#hoursPerWeek", "#startDate", "#text"], bad.All("[data-testid=form-summary] li a").Select(a => a.GetAttribute("href")));
        Assert.Equal(0, f.Web.Booking.Requests.Count(r => r.Method == HttpMethod.Post));
    }

    [Fact]
    public async Task A_valid_counter_posts_the_new_terms_and_the_note_with_the_round_and_a_short_one_uses_total_hours()
    {
        await using var f = await P.Open(P.Booking(P.View("AwaitingPractitioner", "Practitioner", counter: true, round: 4, kind: "ShortTerm"), _ => FakeBackend.Json(P.View("AwaitingClient"))));
        var done = await f.Browser.Submit(PractitionerPath + "?action=counter", "counter", [F("rate", "180"), F("totalHours", "50"), F("durationMonths", "3"), F("startDate", P.StartIso), F("text", "Proponuję inne warunki, dziękuję.")]);
        Assert.Equal(HttpStatusCode.Found, done.Status);
        Assert.Equal(PractitionerPath + "?done=countered", done.Location);
        var post = f.Web.Booking.Requests.Single(r => r.Method == HttpMethod.Post);
        Assert.Equal($"/api/v1/proposals/{P.Id}/counter", post.PathAndQuery);
        using var body = JsonDocument.Parse(post.Body!);
        Assert.Equal(4, body.RootElement.GetProperty("round").GetInt32());
        Assert.Equal("Proponuję inne warunki, dziękuję.", body.RootElement.GetProperty("text").GetString());
        var terms = body.RootElement.GetProperty("terms");
        Assert.Equal((18000, 50, 3, P.StartIso), (terms.GetProperty("rate").GetProperty("amountMinor").GetInt32(), terms.GetProperty("totalHours").GetInt32(), terms.GetProperty("durationMonths").GetInt32(), terms.GetProperty("startDate").GetString()));
        Assert.False(terms.TryGetProperty("hoursPerWeek", out var hw) && hw.ValueKind != JsonValueKind.Null);
    }

    [Fact]
    public async Task A_counter_without_a_note_omits_it_and_contact_details_in_it_are_refused_in_our_words()
    {
        await using var f = await P.Open(P.Booking(P.View("AwaitingPractitioner", "Practitioner", counter: true), r => FakeBackend.Json(P.View("AwaitingClient"))));
        var fields = new[] { F("rate", "180"), F("hoursPerWeek", "10"), F("durationMonths", "3"), F("startDate", P.StartIso) };
        await f.Browser.Submit(PractitionerPath + "?action=counter", "counter", [.. fields, F("text", "")]);
        Assert.False(JsonDocument.Parse(f.Web.Booking.Requests.Last().Body!).RootElement.TryGetProperty("text", out _));

        const string marker = "MARKER-COUNTER-5b2e";
        f.Web.Booking.Respond = P.Booking(P.View("AwaitingPractitioner", "Practitioner", counter: true), _ => FakeBackend.Problem(HttpStatusCode.UnprocessableEntity, "contact_details_not_allowed"));
        var refused = await f.Browser.Submit(PractitionerPath + "?action=counter", "counter", [.. fields, F("text", marker + " zadzwoń 600 123 456")]);
        Assert.Contains("Usuń adresy e-mail", refused.TextOf("#text-error"));
        Assert.DoesNotContain("SERVER-", refused.Html);
        Assert.DoesNotContain(f.Logs.Lines, l => l.Contains(marker));
    }

    [Fact]
    public async Task A_programmer_sees_the_client_by_initial_and_the_company_and_the_practitioner_pages_carry_the_tabs()
    {
        await using var f = await P.Open(P.Booking(P.View("AwaitingPractitioner", "Practitioner", accept: true, counter: true, reject: true, company: "Demo Sp. z o.o.")));
        var page = await f.Browser.GetAsync(PractitionerPath);
        Assert.Contains("Klient J. | Demo Sp. z o.o.", Dd(page).Replace("Anna Demowska | ", ""));
        Assert.Equal(["Propozycje"], page.One("nav[aria-label='Sekcje panelu programisty']")!.QuerySelectorAll("a[aria-current=page]").Select(a => a.TextContent));
        Assert.Equal("noindex,nofollow", page.One("meta[name=robots]")!.GetAttribute("content"));
        Assert.Equal("/gabinet-programisty/propozycje", page.One("p > a[href='/gabinet-programisty/propozycje']")!.GetAttribute("href"));
        Assert.Equal(["Przyjmij warunki", "Zaproponuj inne warunki", "Odmów"], page.All("form[method=get] button").Select(b => b.TextContent));
    }
}
