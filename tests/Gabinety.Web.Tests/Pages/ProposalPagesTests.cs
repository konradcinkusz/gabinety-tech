using System.Net;
using System.Text.Json;
using Gabinety.Web.Api;
using Gabinety.Web.Tests.PractitionerAreaTests;
using Microsoft.Extensions.DependencyInjection;
using static Gabinety.Web.Tests.Pages.ClientAreaFixture;

namespace Gabinety.Web.Tests.Pages;

/// <summary>Synthetic proposals (`demo-`) and the answers a Booking stand-in gives for them (ADR proposal 0013).</summary>
static class P
{
    public static readonly Guid Id = Guid.Parse("018f0000-0000-7000-8000-0000000000c1");
    // The real clock: the sign-in of these tests mints tokens at real time, so a clock fixed in the future would end the session. Dates are computed from today instead.
    public static DateTimeOffset Now => DateTimeOffset.UtcNow;
    public static DateOnly Today => Gabinety.Web.Components.Pages.Shared.PlFormat.TodayWarsaw(DateTimeOffset.UtcNow);
    public static string TodayIso => Today.ToString("yyyy-MM-dd");
    public static string StartIso => Today.AddDays(14).ToString("yyyy-MM-dd");
    public static ProposalTermsDto Terms => new(new MoneyDto(15000, "PLN"), 20, null, 6, Today.AddDays(14));

    public static ProposalViewDto View(string status = "AwaitingPractitioner", string role = "Client", bool accept = false, bool counter = false, bool reject = false, bool withdraw = false,
        string kind = "LongTerm", string? company = null, bool contact = false, string? email = null, int round = 1, ProposalTermsDto? terms = null, IReadOnlyList<ProposalRoundDto>? rounds = null, bool expires = true) =>
        new(Id, kind, status, terms ?? (kind == "LongTerm" ? Terms : Terms with { HoursPerWeek = null, TotalHours = 40, DurationMonths = null }),
            new PractitionerRefDto(Guid.NewGuid(), "Anna Demowska", "demo-anna-demowska"), new ClientRefDto("Klient J."), company,
            rounds ?? [new ProposalRoundDto(1, "Client", "Proposed", terms ?? Terms, "Szukamy programisty do współpracy nad systemem.", Now.AddDays(-1))], round, role,
            expires && status is "AwaitingPractitioner" or "AwaitingClient" ? Now.AddDays(14) : null, accept, counter, reject, withdraw, contact ? new ProposalContactDto(email) : null);

    public static ProposalSummaryDto Row(string status = "AwaitingPractitioner", bool yourTurn = false, string client = "Klient J.", string? company = null, Guid? id = null) =>
        new(id ?? Id, "LongTerm", status, Terms, new PractitionerRefDto(Guid.NewGuid(), "Anna Demowska", "demo-anna-demowska"), new ClientRefDto(client), company, 1, yourTurn, Now.AddHours(-3), Now.AddDays(14));

    public static Func<HttpRequestMessage, HttpResponseMessage> Booking(ProposalViewDto view, Func<HttpRequestMessage, HttpResponseMessage>? post = null) => r =>
        r.Method == HttpMethod.Get && r.RequestUri!.AbsolutePath == $"/api/v1/proposals/{Id}" ? FakeBackend.Json(view)
        : r.Method == HttpMethod.Post && post is not null ? post(r) : new HttpResponseMessage(HttpStatusCode.NotFound);

    public static async Task<ClientAreaFixture> Open(Func<HttpRequestMessage, HttpResponseMessage> booking, Func<HttpRequestMessage, HttpResponseMessage>? directory = null)
    {
        var f = await ClientAreaFixture.SignedIn();
        f.Web.Booking.Respond = booking;
        if (directory is not null) f.Web.Directory.Respond = directory;
        return f;
    }

    public static string Body(Recorded r) => r.Body ?? "";
}

public class ProposalFormPageTests
{
    const string Path = "/propozycja/demo-anna-demowska";

    static PractitionerProfileDto Accepting(bool contract = true) =>
        Profile() with { Contract = contract ? new ContractAvailabilityDto(null, 20, new MoneyDto(11000, "PLN")) : null };

    static Func<HttpRequestMessage, HttpResponseMessage> Directory(PractitionerProfileDto profile) => r => r.RequestUri!.AbsolutePath == "/api/v1/practitioners/demo-anna-demowska"
        ? FakeBackend.Json(profile) : new HttpResponseMessage(HttpStatusCode.NotFound);

    static KeyValuePair<string, string> F(string name, string value) => KeyValuePair.Create(name, value);

    static readonly KeyValuePair<string, string>[] Valid =
        [F("rate", "150"), F("hoursPerWeek", "20"), F("durationMonths", "6"), F("startDate", P.StartIso), F("text", "Szukamy programisty .NET do współpracy nad systemem rezerwacji."), F("companyName", "")];

    [Fact]
    public async Task The_form_for_a_long_term_proposal_has_the_labels_the_hooks_and_no_script()
    {
        await using var f = await P.Open(_ => new HttpResponseMessage(HttpStatusCode.NotFound), Directory(Accepting()));
        var page = await f.Browser.GetAsync(Path);
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal("Propozycja współpracy długoterminowej dla Anna Demowska", page.TextOf("h1"));
        Assert.Equal("noindex,nofollow", page.One("meta[name=robots]")!.GetAttribute("content"));
        var labels = page.All("form label").Select(l => Page.Squash(l.TextContent)).ToList();
        Assert.Equal(["Stawka za godzinę (zł)", "Godzin tygodniowo", "Okres (miesiące)", "Start", "Wiadomość do programisty", "Nazwa firmy (opcjonalnie)"], labels);
        Assert.All(page.All("form label"), l => Assert.NotNull(page.One($"#{l.GetAttribute("for")}"))); // a label points at a real control
        Assert.Equal("date", page.One("#startDate")!.GetAttribute("type"));
        Assert.Equal(P.TodayIso, page.One("#startDate")!.GetAttribute("min")); // today in Warsaw
        Assert.Equal(P.Today.AddMonths(24).ToString("yyyy-MM-dd"), page.One("#startDate")!.GetAttribute("max"));
        Assert.Equal("long", page.One("input[name=kind]")!.GetAttribute("value"));
        Assert.Equal("Wyślij propozycję", page.One("form button[type=submit]")!.TextContent);
        Assert.Empty(page.All("script:not([src])")); // no inline script (CSP)
        Assert.Equal("page", page.One("nav.tabs a[aria-current]")!.GetAttribute("aria-current"));
        Assert.Equal(["Długoterminowa (godziny w tygodniu przez kilka miesięcy)"], page.All("nav.tabs a[aria-current]").Select(a => a.TextContent));
    }

    [Fact]
    public async Task A_short_proposal_asks_for_total_hours_and_months_are_optional()
    {
        await using var f = await P.Open(_ => new HttpResponseMessage(HttpStatusCode.NotFound), Directory(Accepting()));
        var page = await f.Browser.GetAsync(Path + "?kind=short");
        Assert.Equal("Propozycja współpracy dla Anna Demowska", page.TextOf("h1"));
        Assert.Equal(["Stawka za godzinę (zł)", "Liczba godzin", "Okres (miesiące) (opcjonalnie)", "Start", "Wiadomość do programisty", "Nazwa firmy (opcjonalnie)"], page.All("form label").Select(l => Page.Squash(l.TextContent)));
        Assert.Equal("short", page.One("input[name=kind]")!.GetAttribute("value"));
        Assert.Null(page.One("#hoursPerWeek"));
        Assert.NotNull(page.One("#totalHours"));
    }

    [Fact]
    public async Task A_programmer_who_does_not_take_proposals_gets_a_notice_and_no_form()
    {
        await using var f = await P.Open(_ => new HttpResponseMessage(HttpStatusCode.NotFound), Directory(Accepting(contract: false)));
        var page = await f.Browser.GetAsync(Path);
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal("Ten programista nie przyjmuje teraz propozycji współpracy.", Page.Squash(page.TextOf("[data-testid=not-accepting] p")));
        Assert.Empty(page.All("form[method=post]"));
        Assert.Equal("/gabinet/demo-anna-demowska", page.One("[data-testid=not-accepting] a")!.GetAttribute("href"));
    }

    [Fact]
    public async Task An_unknown_programmer_is_404_and_a_directory_that_is_down_is_an_alert_with_a_way_back()
    {
        await using var f = await P.Open(_ => new HttpResponseMessage(HttpStatusCode.NotFound), r => new HttpResponseMessage(HttpStatusCode.NotFound));
        Assert.Equal(HttpStatusCode.NotFound, (await f.Browser.GetAsync("/propozycja/demo-nobody")).Status);
        f.Web.Directory.Respond = FakeBackend.Throw(new HttpRequestException("down"));
        var down = await f.Browser.GetAsync(Path);
        Assert.Equal("alert", down.One("[data-testid=unavailable]")!.GetAttribute("role"));
        Assert.Equal(Path, down.One("[data-testid=unavailable] a")!.GetAttribute("href"));
    }

    [Fact]
    public async Task A_visitor_is_sent_to_sign_in_and_brought_back()
    {
        var web = new WebFactory();
        web.Directory.Respond = Directory(Accepting());
        var page = await web.NewBrowser().GetAsync(Path);
        Assert.Equal(HttpStatusCode.Found, page.Status);
        Assert.StartsWith("/logowanie?redirect=", page.Location);
        Assert.Contains("propozycja", Uri.UnescapeDataString(page.Location!));
        await web.DisposeAsync();
    }

    [Fact]
    public async Task An_empty_form_shows_every_problem_at_once_and_calls_nobody()
    {
        await using var f = await P.Open(_ => new HttpResponseMessage(HttpStatusCode.NotFound), Directory(Accepting()));
        var page = await f.Browser.Submit(Path, "proposal", [F("rate", ""), F("hoursPerWeek", ""), F("durationMonths", ""), F("startDate", ""), F("text", "za krótko")]);
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal(["#rate", "#hoursPerWeek", "#durationMonths", "#startDate", "#text"], page.All("[data-testid=form-summary] li a").Select(a => a.GetAttribute("href")));
        Assert.Equal("Popraw 5 pól", page.TextOf("[data-testid=form-summary] h2"));
        Assert.All(new[] { "rate", "hoursPerWeek", "durationMonths", "startDate", "text" }, id => Assert.NotNull(page.One($"#{id}-error")));
        Assert.Equal("za krótko", page.One("#text")!.TextContent); // what was typed stays
        Assert.Equal(0, f.Web.Booking.Calls);
    }

    [Fact]
    public async Task A_valid_form_posts_the_proposal_with_a_key_and_goes_to_the_new_proposal()
    {
        string? key = null;
        await using var f = await P.Open(r => { key = r.Headers.GetValues("Idempotency-Key").Single(); return FakeBackend.Json(P.View(), HttpStatusCode.Created); }, Directory(Accepting()));
        var page = await f.Browser.Submit(Path, "proposal", [.. Valid.Where(v => v.Key != "companyName"), F("companyName", "Demo Sp. z o.o.")]);
        Assert.Equal(HttpStatusCode.Found, page.Status);
        Assert.Equal($"/moje/propozycje/{P.Id}?done=sent", page.Location);
        var sent = Assert.Single(f.Web.Booking.Requests);
        Assert.Equal((HttpMethod.Post, "/api/v1/proposals"), (sent.Method, sent.PathAndQuery));
        Assert.True(Guid.TryParse(key, out _));
        using var body = JsonDocument.Parse(sent.Body!);
        var root = body.RootElement;
        Assert.Equal("LongTerm", root.GetProperty("kind").GetString());
        Assert.Equal("Demo Sp. z o.o.", root.GetProperty("companyName").GetString());
        Assert.Equal("Szukamy programisty .NET do współpracy nad systemem rezerwacji.", root.GetProperty("text").GetString());
        var terms = root.GetProperty("terms");
        Assert.Equal((15000, 20, 6, P.StartIso), (terms.GetProperty("rate").GetProperty("amountMinor").GetInt32(), terms.GetProperty("hoursPerWeek").GetInt32(), terms.GetProperty("durationMonths").GetInt32(), terms.GetProperty("startDate").GetString()));
        Assert.False(terms.TryGetProperty("totalHours", out var th) && th.ValueKind != JsonValueKind.Null);
        Assert.DoesNotContain("Szukamy", page.Location);
    }

    [Fact]
    public async Task A_short_proposal_sends_total_hours_and_an_empty_company_is_left_out()
    {
        await using var f = await P.Open(_ => FakeBackend.Json(P.View(kind: "ShortTerm"), HttpStatusCode.Created), Directory(Accepting()));
        var page = await f.Browser.Submit(Path + "?kind=short", "proposal", [F("rate", "99,50"), F("totalHours", "40"), F("durationMonths", ""), F("startDate", P.TodayIso), F("text", "Krótka współpraca nad jednym modułem aplikacji.")]);
        Assert.Equal(HttpStatusCode.Found, page.Status);
        using var body = JsonDocument.Parse(f.Web.Booking.Requests.Single().Body!);
        Assert.Equal("ShortTerm", body.RootElement.GetProperty("kind").GetString());
        Assert.Equal(40, body.RootElement.GetProperty("terms").GetProperty("totalHours").GetInt32());
        Assert.Equal(9950, body.RootElement.GetProperty("terms").GetProperty("rate").GetProperty("amountMinor").GetInt32());
        Assert.False(body.RootElement.TryGetProperty("companyName", out _));
    }

    [Fact]
    public async Task A_retry_of_the_same_form_keeps_its_key_and_another_body_gets_a_new_one()
    {
        var keys = new List<string>();
        await using var f = await P.Open(r => { keys.Add(r.Headers.GetValues("Idempotency-Key").Single()); return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable); }, Directory(Accepting()));

        var first = await f.Browser.Submit(Path, "proposal", Valid);
        Assert.Equal(HttpStatusCode.OK, first.Status); // the failure is shown on the form, not a redirect
        var (key, fingerprint) = (first.One("input[name=attemptKey]")!.GetAttribute("value")!, first.One("input[name=attemptFingerprint]")!.GetAttribute("value")!);
        Assert.Equal(keys[0], key);
        Assert.DoesNotContain("Szukamy", fingerprint); // only a hash goes back to the browser

        await f.Browser.Submit(Path, "proposal", [.. Valid, F("attemptKey", key), F("attemptFingerprint", fingerprint)]); // the user presses the button again
        Assert.Equal(keys[0], keys[1]);

        await f.Browser.Submit(Path, "proposal", [.. Valid.Where(v => v.Key != "text"), F("text", "Zupełnie inna wiadomość do programisty."), F("attemptKey", key), F("attemptFingerprint", fingerprint)]);
        Assert.NotEqual(keys[0], keys[2]);
    }

    [Fact]
    public async Task Contact_details_in_the_message_are_refused_on_the_message_and_the_company_in_our_words()
    {
        const string marker = "MARKER-WIADOMOSC-3f9a";
        await using var f = await P.Open(_ => FakeBackend.Problem(HttpStatusCode.UnprocessableEntity, "contact_details_not_allowed"), Directory(Accepting()));
        var page = await f.Browser.Submit(Path, "proposal", [.. Valid.Where(v => v.Key is not ("text" or "companyName")), F("text", marker + " napisz na jan@firma.pl w sprawie współpracy"), F("companyName", marker + "-firma")]);
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal(["#text", "#companyName"], page.All("[data-testid=form-summary] li a").Select(a => a.GetAttribute("href")));
        Assert.Contains("Usuń adresy e-mail", page.TextOf("#text-error"));
        Assert.DoesNotContain("SERVER-", page.Html);
        Assert.DoesNotContain(f.Logs.Lines, l => l.Contains(marker));
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict, "proposal_exists", "Masz już otwartą propozycję dla tego programisty. Odpowiedz w niej albo ją wycofaj.")]
    [InlineData(HttpStatusCode.UnprocessableEntity, "own_profile", "Nie możesz złożyć propozycji samemu sobie.")]
    [InlineData(HttpStatusCode.UnprocessableEntity, "practitioner_not_available", "Ten programista nie przyjmuje teraz propozycji współpracy.")]
    [InlineData(HttpStatusCode.TooManyRequests, "rate_limited", "Wysłano zbyt wiele propozycji. Spróbuj ponownie później.")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "dependency_unavailable", "Ta część serwisu jest chwilowo niedostępna. Spróbuj ponownie za chwilę.")]
    public async Task A_refusal_of_the_service_is_said_in_our_words_and_the_form_stays(HttpStatusCode status, string code, string text)
    {
        await using var f = await P.Open(_ => FakeBackend.Problem(status, code), Directory(Accepting()));
        var page = await f.Browser.Submit(Path, "proposal", Valid);
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal(text, Page.Squash(page.TextOf("[data-testid=proposal-error]")));
        Assert.Equal("alert", page.One("[data-testid=proposal-error]")!.GetAttribute("role"));
        Assert.Equal("150", page.One("#rate")!.GetAttribute("value")); // what was typed stays
        Assert.DoesNotContain("SERVER-", page.Html);
    }

    [Fact]
    public async Task A_field_the_service_names_is_shown_under_that_field()
    {
        await using var f = await P.Open(_ => new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
        {
            Content = new StringContent("{\"code\":\"validation_failed\",\"errors\":{\"terms.startDate\":[\"x\"]}}", System.Text.Encoding.UTF8, "application/problem+json"),
        }, Directory(Accepting()));
        var page = await f.Browser.Submit(Path, "proposal", Valid);
        Assert.Equal(["#startDate"], page.All("[data-testid=form-summary] li a").Select(a => a.GetAttribute("href")));
        Assert.Contains("Podaj dzień startu", page.TextOf("#startDate-error"));
    }

    [Fact]
    public async Task A_session_that_ended_while_posting_goes_to_sign_in()
    {
        await using var f = await P.Open(_ => FakeBackend.Problem(HttpStatusCode.Unauthorized, "unauthenticated"), Directory(Accepting()));
        var page = await f.Browser.Submit(Path, "proposal", Valid);
        Assert.Equal(HttpStatusCode.Found, page.Status);
        Assert.StartsWith("/logowanie?redirect=", page.Location);
    }

    [Fact]
    public async Task The_message_never_reaches_a_log()
    {
        const string marker = "MARKER-LOG-77c1";
        await using var f = await P.Open(_ => FakeBackend.Problem(HttpStatusCode.InternalServerError, "boom"), Directory(Accepting()));
        await f.Browser.Submit(Path, "proposal", [.. Valid.Where(v => v.Key != "text"), F("text", marker + " szukamy programisty do współpracy.")]);
        Assert.DoesNotContain(f.Logs.Lines, l => l.Contains(marker));
    }
}
