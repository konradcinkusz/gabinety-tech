using System.Net;
using System.Text.Json;
using Gabinety.Web.Api;
using static Gabinety.Web.Tests.Pages.ClientAreaFixture;

namespace Gabinety.Web.Tests.Pages;

/// <summary>The booking form against the real host: routes, redirects, antiforgery, the request the Booking service receives, and what the visitor reads when it refuses (journeys J3 and J4 read these hooks).</summary>
public class BookingFormTests
{
    const string Brief = "Potrzebuję przeglądu architektury prostego prototypu, dane syntetyczne.";
    static readonly Guid NewBooking = Guid.Parse("018f0000-0000-7000-8000-0000000000c1");

    static HttpResponseMessage Created(string url = "/checkout/mock/018f0000-0000-7000-8000-0000000000c1") =>
        FakeBackend.Json(new CreateBookingResponseDto(NewBooking, "PendingPayment", DateTimeOffset.UtcNow.AddMinutes(10), url), HttpStatusCode.Created);

    static async Task<ClientAreaFixture> Open(Func<HttpRequestMessage, HttpResponseMessage>? booking = null, Func<HttpRequestMessage, HttpResponseMessage>? assistant = null)
    {
        var f = await ClientAreaFixture.SignedIn();
        f.Web.Booking.Respond = booking ?? (_ => Created());
        f.Web.Assistant.Respond = assistant ?? (_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        return f;
    }

    static (string Name, string Value) Intent(string intent) => ("Model.Intent", intent);

    [Fact]
    public async Task A_visitor_is_sent_to_sign_in_and_comes_back_to_the_same_slot_and_nothing_is_asked_of_the_backends()
    {
        await using var f = await ClientAreaFixture.Visitor();
        var page = await f.Browser.GetAsync(BookingUrl);
        Assert.Equal(HttpStatusCode.Found, page.Status);
        Assert.StartsWith("/logowanie?redirect=%2Frezerwacja%2F", page.Location);
        Assert.Contains("gabinet%3Ddemo-anna-demowska", page.Location);
        Assert.Contains("start%3D2026-12-08T11%253A00%253A00Z", page.Location);
        Assert.Empty(f.Web.Directory.Requests);
    }

    [Fact]
    public async Task The_form_has_the_hooks_the_journeys_use_and_a_summary_with_the_name_the_time_and_the_price()
    {
        await using var f = await Open();
        var page = await f.Browser.GetAsync(BookingUrl);
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal(["Rezerwacja konsultacji"], page.All("h1").Select(h => h.TextContent));
        Assert.Equal("Opisz problem", page.One("label[for=brief]")!.TextContent);
        Assert.Equal("textarea", page.One("#brief")!.TagName.ToLowerInvariant());
        Assert.Equal(["Pomóż mi opisać problem", "Zapłać 250,00 zł"], page.All("form button").Select(b => b.TextContent));
        Assert.Single(page.All("button"), b => b.TextContent.Contains("Zapłać", StringComparison.OrdinalIgnoreCase)); // J3: getByRole("button", { name: "Zapłać" }) must match exactly one
        var summary = page.One(".booking-summary")!;
        Assert.Equal("Podsumowanie", summary.QuerySelector("h2")!.TextContent);
        Assert.Equal(["Konsultacja", "Programista", "Termin", "Czas trwania", "Do zapłaty"], summary.QuerySelectorAll("dt").Select(d => d.TextContent));
        Assert.Equal(["Audyt kodu demo", "Anna Demowska", "wtorek, 8 grudnia, 12:00–13:00", "60 min", "250,00 zł"], summary.QuerySelectorAll("dd").Select(d => d.TextContent));
        Assert.Contains("Europe/Warsaw", summary.TextContent);
        Assert.Contains("Termin jest wstrzymany na 10 minut od kliknięcia „Zapłać”.", summary.QuerySelector(".callout--info")!.TextContent);
        Assert.Single(page.All("*"), e => e.ChildElementCount == 0 && e.TextContent == "Audyt kodu demo"); // J3: getByText(offering.title) must be one element
        Assert.NotNull(page.One("form input[name=__RequestVerificationToken]")); // antiforgery
        Assert.Empty(page.All("script:not([src])")); // no inline script (CSP)
    }

    [Fact]
    public async Task The_summary_comes_before_the_form_in_the_dom_so_on_the_phone_it_stays_above_the_pay_button()
    {
        await using var f = await Open();
        var html = (await f.Browser.GetAsync(BookingUrl)).Html;
        Assert.True(html.IndexOf("booking-layout__summary", StringComparison.Ordinal) < html.IndexOf("booking-layout__form", StringComparison.Ordinal));
        Assert.Contains("min-width: 56rem", File.ReadAllText(Path.Combine(Repo.Web, "Components", "Pages", "Booking", "BookingFormPage.razor.css"))); // the two columns of DESIGN-CONTRACT 3.5
    }

    [Fact]
    public async Task The_backend_is_asked_for_the_profile_of_the_slug_and_nothing_else_for_a_get()
    {
        await using var f = await Open();
        await f.Browser.GetAsync(BookingUrl);
        Assert.Equal(["/api/v1/practitioners/demo-anna-demowska"], f.Web.Directory.Requests.Where(r => r.PathAndQuery != "/api/v1/me").Select(r => r.PathAndQuery));
        Assert.Empty(f.Web.Booking.Requests);
        Assert.Empty(f.Web.Assistant.Requests);
    }

    [Theory]
    [InlineData("")] [InlineData("&gabinet=")] [InlineData("&gabinet=%20")]
    public async Task Without_a_profile_slug_the_page_says_the_slot_is_missing_and_goes_to_the_search(string slug)
    {
        await using var f = await Open();
        var page = await f.Browser.GetAsync($"/rezerwacja/{OfferingId}?start=2026-12-08T11:00:00Z{slug}");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Contains("Nie wybrano terminu albo adres jest niepełny.", page.Text);
        Assert.Equal("/programisci", page.One("a.btn")!.GetAttribute("href"));
        Assert.Empty(page.All("form"));
    }

    [Theory]
    [InlineData("")] [InlineData("&start=")] [InlineData("&start=jutro")]
    public async Task Without_a_valid_start_the_page_says_so_and_goes_back_to_the_profile(string start)
    {
        await using var f = await Open();
        var page = await f.Browser.GetAsync($"/rezerwacja/{OfferingId}?gabinet=demo-anna-demowska{start}");
        Assert.Contains("Nie wybrano terminu albo adres jest niepełny.", page.Text);
        Assert.Equal("/gabinet/demo-anna-demowska", page.One("a.btn")!.GetAttribute("href"));
        Assert.Empty(page.All("form"));
    }

    [Fact]
    public async Task An_unknown_profile_and_an_offering_that_is_not_in_it_are_a_404()
    {
        await using var f = await Open();
        f.Web.Directory.Respond = r => r.RequestUri!.AbsolutePath.EndsWith("demo-anna-demowska") ? FakeBackend.Json(Profile()) : FakeBackend.Problem(HttpStatusCode.NotFound, "not_found");
        Assert.Equal(HttpStatusCode.NotFound, (await f.Browser.GetAsync($"/rezerwacja/{OfferingId}?start=2026-12-08T11:00:00Z&gabinet=demo-nikt")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await f.Browser.GetAsync($"/rezerwacja/{Guid.NewGuid()}?start=2026-12-08T11:00:00Z&gabinet=demo-anna-demowska")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await f.Browser.GetAsync("/rezerwacja/nie-guid?start=2026-12-08T11:00:00Z&gabinet=demo-anna-demowska")).Status);
    }

    [Fact]
    public async Task When_the_directory_is_down_the_page_says_so_in_an_alert_with_a_retry_and_the_title_stays()
    {
        await using var f = await Open();
        f.Web.Directory.Respond = FakeBackend.Throw(new HttpRequestException("down"));
        var page = await f.Browser.GetAsync(BookingUrl);
        Assert.Equal("alert", page.One("[data-testid=unavailable]")!.GetAttribute("role"));
        Assert.Equal(BookingUrl, page.One("[data-testid=unavailable] a")!.GetAttribute("href"));
        Assert.Equal("Rezerwacja konsultacji", page.TextOf("h1"));
        Assert.Empty(page.All("form"));
    }

    // ---- validation (DESIGN-CONTRACT 5.5, G-9, P-BOOK-2)

    [Fact]
    public async Task A_short_brief_is_one_summary_one_inline_error_linked_to_the_field_and_the_backend_is_not_asked()
    {
        await using var f = await Open();
        var page = await f.Post(BookingUrl, "booking", Intent("pay"), ("Model.Brief", "za krótki"));
        Assert.Equal(HttpStatusCode.OK, page.Status);
        var summary = page.One(".form-summary")!;
        Assert.Equal("alert", summary.GetAttribute("role"));
        Assert.Equal("Popraw 1 pole", summary.QuerySelector("h2")!.TextContent);
        Assert.Equal("#brief", summary.QuerySelector("a")!.GetAttribute("href"));
        Assert.Equal("Opis musi mieć co najmniej 20 znaków.", summary.QuerySelector("a")!.TextContent);
        var field = page.One("#brief")!;
        Assert.Equal("true", field.GetAttribute("aria-invalid"));
        Assert.Equal("brief-hint brief-error", field.GetAttribute("aria-describedby"));
        Assert.Equal("Błąd: Opis musi mieć co najmniej 20 znaków.", Page.Squash(page.TextOf("#brief-error")));
        Assert.Contains("field--invalid", page.One("#brief")!.ParentElement!.ClassName);
        Assert.NotNull(field.GetAttribute("autofocus")); // focus goes to the one invalid control
        Assert.Equal("9 / 2000", page.TextOf(".field__counter")); // the counter sits inside the field, under the control
        Assert.Equal("za krótki", field.TextContent); // what was typed is still there
        Assert.Empty(f.Web.Booking.Requests);
    }

    [Fact]
    public async Task A_brief_that_is_too_long_says_which_side_is_wrong()
    {
        await using var f = await Open();
        var page = await f.Post(BookingUrl, "booking", Intent("pay"), ("Model.Brief", new string('a', 2001)));
        Assert.Equal("Opis może mieć najwyżej 2000 znaków.", page.TextOf(".form-summary a"));
        Assert.Empty(f.Web.Booking.Requests);
    }

    // ---- paying

    [Fact]
    public async Task Pay_sends_one_request_with_a_key_a_utc_start_and_the_trimmed_brief_then_redirects_to_the_checkout_of_the_booking()
    {
        await using var f = await Open();
        var page = await f.Post(BookingUrl, "booking", Intent("pay"), ("Model.Brief", $"  {Brief}  "));
        Assert.Equal(HttpStatusCode.Found, page.Status);
        Assert.Equal($"/checkout/mock/{NewBooking}", page.Location);
        var request = Assert.Single(f.Web.Booking.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/api/v1/bookings", request.PathAndQuery);
        Assert.StartsWith("Bearer eyJ", request.Authorization);
        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal(OfferingId.ToString(), body.RootElement.GetProperty("offeringId").GetString());
        Assert.Equal("2026-12-08T11:00:00+00:00", body.RootElement.GetProperty("startUtc").GetDateTimeOffset().ToString("yyyy-MM-ddTHH:mm:sszzz"));
        Assert.EndsWith("Z", body.RootElement.GetProperty("startUtc").GetString()); // the service insists on a UTC instant
        Assert.Equal(Brief, body.RootElement.GetProperty("brief").GetProperty("text").GetString());
        Assert.False(body.RootElement.GetProperty("brief").GetProperty("aiAssisted").GetBoolean());
        Assert.Null(body.RootElement.GetProperty("brief").GetProperty("problemSlug").GetString());
    }

    [Fact]
    public async Task The_idempotency_key_reaches_the_service_as_a_header_and_is_a_guid()
    {
        await using var f = await Open();
        var seen = new List<string?>();
        f.Web.Booking.Respond = r => { seen.Add(r.Headers.TryGetValues("Idempotency-Key", out var v) ? v.Single() : null); return Created(); };
        await f.Post(BookingUrl, "booking", Intent("pay"), ("Model.Brief", Brief));
        Assert.True(Guid.TryParse(Assert.Single(seen), out var key) && key != Guid.Empty);
    }

    [Fact]
    public async Task A_stripe_checkout_url_is_followed_as_it_is_and_the_page_flow_does_not_assume_the_mock()
    {
        await using var f = await Open(booking: _ => Created("https://checkout.stripe.com/c/pay/cs_test_demo#fid"));
        var page = await f.Post(BookingUrl, "booking", Intent("pay"), ("Model.Brief", Brief));
        Assert.Equal(HttpStatusCode.Found, page.Status);
        Assert.Equal("https://checkout.stripe.com/c/pay/cs_test_demo#fid", page.Location);
    }

    [Theory]
    [InlineData("http://evil.example/pay")] [InlineData("//evil.example/pay")] [InlineData("javascript:alert(1)")] [InlineData("")]
    public async Task A_checkout_url_that_is_not_the_site_or_an_https_page_is_refused_and_the_visitor_stays_with_a_message(string url)
    {
        await using var f = await Open(booking: _ => Created(url));
        var page = await f.Post(BookingUrl, "booking", Intent("pay"), ("Model.Brief", Brief));
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Null(page.Location);
        Assert.Equal("alert", page.One("[data-testid=booking-error]")!.GetAttribute("role"));
        Assert.Contains("Nie udało się przejść do płatności.", page.Text);
        Assert.Equal(Brief, page.One("#brief")!.TextContent);
    }

    [Theory]
    [InlineData(409, "slot_taken", "Ten termin został już zajęty albo nie jest dostępny. Wybierz inny termin.", true)]
    [InlineData(422, "slot_unavailable", "Ten termin został już zajęty albo nie jest dostępny. Wybierz inny termin.", true)]
    [InlineData(422, "offering_inactive", "Ta konsultacja nie jest już dostępna.", true)]
    [InlineData(422, "own_offering", "Nie możesz zarezerwować własnej konsultacji.", true)]
    [InlineData(422, "practitioner_not_bookable", "Ten programista nie przyjmuje teraz rezerwacji.", true)]
    [InlineData(422, "practitioner_payments_not_ready", "Ten programista nie może jeszcze przyjmować płatności, więc nie można zarezerwować tej konsultacji. Spróbuj z innym programistą albo wróć za jakiś czas.", true)]
    [InlineData(429, "rate_limited", "Zbyt wiele prób. Odczekaj chwilę i spróbuj ponownie.", false)]
    [InlineData(503, "dependency_unavailable", "Nie udało się połączyć z usługą. Spróbuj ponownie: nie zostaniesz obciążony dwa razy.", false)]
    [InlineData(400, "validation_failed", "Nie udało się utworzyć rezerwacji. Sprawdź dane i spróbuj ponownie.", false)]
    public async Task A_refusal_of_the_service_is_an_alert_in_our_words_with_the_way_back_to_the_profile_when_it_helps(int status, string code, string text, bool profile)
    {
        await using var f = await Open(booking: _ => FakeBackend.Problem((HttpStatusCode)status, code));
        var page = await f.Post(BookingUrl, "booking", Intent("pay"), ("Model.Brief", Brief));
        var alert = page.One("[data-testid=booking-error]")!;
        Assert.Equal("alert", alert.GetAttribute("role"));
        Assert.Equal(text, alert.QuerySelector("p")!.TextContent);
        Assert.Equal(profile ? "/gabinet/demo-anna-demowska" : null, alert.QuerySelector("a")?.GetAttribute("href"));
        Assert.DoesNotContain("SERVER-TITLE-MUST-NOT-APPEAR", page.Html);
        Assert.DoesNotContain("SERVER-DETAIL-MUST-NOT-APPEAR", page.Html);
        Assert.Equal(Brief, page.One("#brief")!.TextContent); // nothing the visitor typed is lost
    }

    [Fact]
    public async Task A_backend_that_does_not_answer_is_the_unavailable_text_and_no_retry_of_the_write_happens_by_itself()
    {
        await using var f = await Open(booking: FakeBackend.Throw(new HttpRequestException("down")));
        var page = await f.Post(BookingUrl, "booking", Intent("pay"), ("Model.Brief", Brief));
        Assert.Contains("nie zostaniesz obciążony dwa razy", page.TextOf("[data-testid=booking-error]"));
        Assert.Equal(1, f.Web.Booking.Calls); // SPEC 5 rule 8: a write is never retried by the client
    }

    [Fact]
    public async Task A_401_goes_to_sign_in_and_comes_back_to_this_slot()
    {
        await using var f = await Open(booking: _ => FakeBackend.Problem(HttpStatusCode.Unauthorized, "unauthenticated"));
        var page = await f.Post(BookingUrl, "booking", Intent("pay"), ("Model.Brief", Brief));
        Assert.Equal(HttpStatusCode.Found, page.Status);
        Assert.StartsWith("/logowanie?redirect=%2Frezerwacja%2F", page.Location);
    }

    // ---- one key per attempt (CONTRACTS 0)

    [Fact]
    public async Task A_retry_of_the_same_request_reuses_the_key_and_a_changed_brief_gets_a_new_one()
    {
        await using var f = await Open(booking: _ => FakeBackend.Problem(HttpStatusCode.ServiceUnavailable, "dependency_unavailable"));
        var keys = new List<string>();
        f.Web.Booking.Respond = r => { keys.Add(r.Headers.GetValues("Idempotency-Key").Single()); return FakeBackend.Problem(HttpStatusCode.ServiceUnavailable, "dependency_unavailable"); };

        var first = await f.Post(BookingUrl, "booking", Intent("pay"), ("Model.Brief", Brief));
        var key = first.One("input[name='Model.AttemptKey']")!.GetAttribute("value")!;
        var fingerprint = first.One("input[name='Model.AttemptFingerprint']")!.GetAttribute("value")!;
        Assert.Equal(keys[0], key);
        Assert.DoesNotContain(Brief, fingerprint);

        // the visitor presses the button again on the page they got back: the hidden fields travel
        await PostAgain(f, key, fingerprint, Brief);
        Assert.Equal([key, key], keys);

        await PostAgain(f, key, fingerprint, Brief + " Dopisek.");
        Assert.NotEqual(key, keys[2]); // the same key with another body would be a 409
        Assert.True(Guid.TryParse(keys[2], out _));
    }

    static async Task PostAgain(ClientAreaFixture f, string key, string fingerprint, string brief) =>
        await f.Post(BookingUrl, "booking", Intent("pay"), ("Model.Brief", brief), ("Model.AttemptKey", key), ("Model.AttemptFingerprint", fingerprint));

    // ---- the assistant (J4)

    static HttpResponseMessage Suggestion(string summary = "Prototyp z modelem językowym: zakres, koszty, ryzyka.", string slug = "audyt-kodu", string[]? questions = null) =>
        FakeBackend.Json(new BriefSuggestionDto(slug, [], summary, questions ?? ["Jaki jest budżet?"], 60, "high"));

    [Fact]
    public async Task An_assistant_that_is_down_leaves_the_plain_note_no_suggestion_and_the_brief_untouched_J4()
    {
        await using var f = await Open(assistant: _ => FakeBackend.Problem(HttpStatusCode.ServiceUnavailable, "assistant_unavailable"));
        var page = await f.Post(BookingUrl, "booking", Intent("assist"), ("Model.Brief", Brief + " [[unavailable]]"));
        Assert.Equal("Asystent jest chwilowo niedostępny. Opisz problem samodzielnie i zarezerwuj termin.", page.TextOf(".assist .callout"));
        Assert.Empty(page.All("[data-testid=proposal]"));
        Assert.DoesNotContain("Sugerowana kategoria", page.Text);
        Assert.DoesNotContain("Dodaj propozycję do opisu", page.Text);
        Assert.Equal(Brief + " [[unavailable]]", page.One("#brief")!.TextContent);
        Assert.NotNull(page.One("button[value=pay]")); // the form still books
    }

    [Theory]
    [InlineData(429, "rate_limited", "Zbyt wiele próśb do asystenta. Spróbuj za chwilę albo opisz problem samodzielnie.")]
    [InlineData(500, "http_500", "Asystent nie mógł przygotować propozycji. Opisz problem samodzielnie.")]
    public async Task Other_assistant_failures_have_their_own_note(int status, string code, string text)
    {
        await using var f = await Open(assistant: _ => FakeBackend.Problem((HttpStatusCode)status, code));
        var page = await f.Post(BookingUrl, "booking", Intent("assist"), ("Model.Brief", Brief));
        Assert.Equal(text, page.TextOf(".assist .callout"));
    }

    [Fact]
    public async Task A_brief_that_is_too_short_for_the_assistant_says_why_and_the_assistant_is_not_called()
    {
        await using var f = await Open(assistant: _ => Suggestion());
        var page = await f.Post(BookingUrl, "booking", Intent("assist"), ("Model.Brief", "krótko"));
        Assert.Equal("Najpierw opisz problem w co najmniej 20 znakach, wtedy asystent będzie mógł pomóc.", page.TextOf(".assist .callout"));
        Assert.Empty(f.Web.Assistant.Requests);
    }

    [Fact]
    public async Task A_suggestion_is_shown_as_an_editable_proposal_with_its_category_and_questions_and_nothing_is_added_to_the_brief()
    {
        await using var f = await Open(assistant: _ => Suggestion());
        var page = await f.Post(BookingUrl, "booking", Intent("assist"), ("Model.Brief", Brief));
        var proposal = page.One("[data-testid=proposal]")!;
        Assert.Equal("Propozycja opisu (możesz ją edytować)", proposal.QuerySelector("label")!.TextContent);
        Assert.Equal("Prototyp z modelem językowym: zakres, koszty, ryzyka.", proposal.QuerySelector("textarea")!.TextContent);
        Assert.Equal("Sugerowana kategoria: Audyt kodu", proposal.QuerySelector("p.assist__category")!.TextContent.Trim()); // the name, not the slug
        Assert.Equal("Audyt kodu", proposal.QuerySelector(".badge--info")!.TextContent);
        Assert.Contains("card", proposal.ClassName); // a clearly separated block inside the form
        Assert.NotNull(proposal.Closest("form"));
        Assert.Equal("Warto jeszcze opisać", proposal.QuerySelector("h3")!.TextContent);
        Assert.Contains("Jaki jest budżet?", proposal.QuerySelectorAll("ul > li").Select(li => li.TextContent));
        var buttons = page.All("form button");
        Assert.Contains("Zapłać", buttons[^1].TextContent); // the pay button stays the last element of the form
        Assert.Equal(buttons[^1], page.All("form *").Last(e => e.TagName == "BUTTON"));
        Assert.Single(page.All("button"), b => b.TextContent.Contains("Zapłać", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Dodaj propozycję do opisu", proposal.QuerySelector("button")!.TextContent);
        Assert.Equal(Brief, page.One("#brief")!.TextContent); // only the click adds it
        var call = Assert.Single(f.Web.Assistant.Requests);
        Assert.Equal("/api/v1/assistant/brief", call.PathAndQuery);
        Assert.Equal(Brief, JsonDocument.Parse(call.Body!).RootElement.GetProperty("text").GetString());
        Assert.StartsWith("Bearer eyJ", call.Authorization);
    }

    [Fact]
    public async Task Applying_the_proposal_appends_the_edited_text_and_the_booking_is_then_ai_assisted_with_the_problem_slug()
    {
        await using var f = await Open(assistant: _ => Suggestion());
        var applied = await f.Post(BookingUrl, "booking", Intent("apply"), ("Model.Brief", Brief), ("Model.Proposal", "  Moja poprawiona wersja propozycji.  "), ("Model.SuggestedSlug", "audyt-kodu"));
        Assert.Contains("Propozycja została dodana do opisu.", applied.Text);
        Assert.Equal(Brief + "\n\nMoja poprawiona wersja propozycji.", applied.One("#brief")!.TextContent.Replace("\r\n", "\n"));
        Assert.Empty(applied.All("[data-testid=proposal]"));
        Assert.Equal("audyt-kodu", applied.One("input[name='Model.AppliedSlug']")!.GetAttribute("value"));

        // the next post (pay) carries the applied slug
        await f.Post(BookingUrl, "booking", Intent("pay"), ("Model.Brief", Brief + "\n\nMoja poprawiona wersja propozycji."), ("Model.AppliedSlug", "audyt-kodu"));
        using var body = JsonDocument.Parse(f.Web.Booking.Requests.Single().Body!);
        Assert.True(body.RootElement.GetProperty("brief").GetProperty("aiAssisted").GetBoolean());
        Assert.Equal("audyt-kodu", body.RootElement.GetProperty("brief").GetProperty("problemSlug").GetString());
    }

    [Fact]
    public async Task An_empty_proposal_adds_nothing()
    {
        await using var f = await Open();
        var page = await f.Post(BookingUrl, "booking", Intent("apply"), ("Model.Brief", Brief), ("Model.Proposal", "   "), ("Model.SuggestedSlug", "audyt-kodu"));
        Assert.Equal(Brief, page.One("#brief")!.TextContent);
        Assert.DoesNotContain("Propozycja została dodana", page.Text);
    }

    // ---- the brief is user free text: never in a log (SPEC 5 rule 2)

    [Fact]
    public async Task The_brief_is_never_written_to_a_log_whatever_happens()
    {
        var userText = "Znacznik opisu brief reszta opisu problemu";
        await using var f = await Open(booking: _ => FakeBackend.Problem(HttpStatusCode.UnprocessableEntity, "slot_taken"), assistant: _ => Suggestion(summary: "Znacznik podpowiedzi asystenta"));
        await f.Post(BookingUrl, "booking", Intent("assist"), ("Model.Brief", userText));
        await f.Post(BookingUrl, "booking", Intent("pay"), ("Model.Brief", userText));
        f.Web.Booking.Respond = FakeBackend.Throw(new HttpRequestException("boom"));
        await f.Post(BookingUrl, "booking", Intent("pay"), ("Model.Brief", userText));
        Assert.NotEmpty(f.Logs.Lines);
        Assert.DoesNotContain(f.Logs.Lines, l => l.Contains("Znacznik opisu", StringComparison.Ordinal) || l.Contains("Znacznik podpowiedzi", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_post_without_the_antiforgery_token_is_refused()
    {
        await using var f = await Open();
        var request = new HttpRequestMessage(HttpMethod.Post, BookingUrl) { Content = new FormUrlEncodedContent([new("_handler", "booking"), new("Model.Intent", "pay"), new("Model.Brief", Brief)]) };
        var page = await f.Browser.SendAsync(request);
        Assert.True((int)page.Status is 400 or 403 or 404, $"{(int)page.Status}");
        Assert.Empty(f.Web.Booking.Requests);
    }
}
