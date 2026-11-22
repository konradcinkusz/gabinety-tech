using System.Net;
using System.Text.Json;
using Gabinety.Web.Api;
using static Gabinety.Web.Tests.Pages.ClientAreaFixture;

namespace Gabinety.Web.Tests.Pages;

/// <summary>The booking detail and its actions (journeys J3, J5, J6): what is shown for each state, what the cancel dialog says (F-11), and what each action sends.</summary>
public class BookingDetailTests
{
    static readonly string Detail_ = $"/moje/rezerwacje/{BookingId}";
    static readonly string Client = Detail_ + "?role=client";

    static async Task<ClientAreaFixture> Open(BookingDetailDto? booking = null, Func<HttpRequestMessage, HttpResponseMessage>? post = null)
    {
        var f = await ClientAreaFixture.SignedIn();
        f.Web.Booking.Respond = r => r.Method == HttpMethod.Post && post is not null ? post(r) : FakeBackend.Json(booking ?? Detail());
        return f;
    }

    static string Values(Page page) => string.Join(" | ", page.All(".dl dd").Select(d => Page.Squash(d.TextContent)));

    // ---- what is shown

    [Fact]
    public async Task A_confirmed_paid_booking_shows_every_value_as_a_definition_the_journeys_can_find()
    {
        await using var f = await Open(Detail(meeting: "https://meet.example/sala"));
        var page = await f.Browser.GetAsync(Client);
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal(["Szczegóły rezerwacji"], page.All("h1").Select(h => h.TextContent));
        Assert.Equal("Audyt kodu demo", page.One("h2")!.TextContent); // the harness waits for the first h2
        Assert.Equal(["Programista", "Termin", "Czas trwania", "Status", "Cena", "Płatność"], page.All(".dl dt").Select(d => d.TextContent));
        var dd = page.All(".dl dd").Select(d => Page.Squash(d.TextContent)).ToList();
        Assert.Equal(["Anna Demowska", "wtorek, 8 grudnia, 12:00–13:00Godziny w czasie polskim (Europe/Warsaw).", "60 min", "Potwierdzona", "250,00 zł", "Opłacona"], dd);
        Assert.All(page.All(".dl dd"), d => Assert.Equal("dd", d.TagName.ToLowerInvariant())); // role "definition"
        Assert.Equal(["badge badge--info"], page.All(".dl dd:nth-of-type(4) .badge").Select(b => b.ClassName));
        Assert.Equal("badge badge--success", page.One(".dl dd:nth-of-type(6) .badge")!.ClassName);
        Assert.DoesNotContain(page.All("*"), e => e.TextContent.Contains("Zwrócono:") && e.ChildElementCount == 0); // J5: no refund line without a refund
        Assert.Equal("https://meet.example/sala", page.One("a[target=_blank]")!.GetAttribute("href"));
        Assert.Contains("Opis problemu demo, syntetyczny.", page.TextOf("p.brief"));
        Assert.Equal("Twój opis problemu", page.All("h2").Last().TextContent);
    }

    [Fact]
    public async Task For_a_practitioner_the_other_party_is_the_client_and_the_brief_is_the_clients()
    {
        await using var f = await Open();
        var page = await f.Browser.GetAsync(Detail_ + "?role=practitioner");
        Assert.Equal(["Klient", "Termin", "Czas trwania", "Status", "Cena", "Płatność"], page.All(".dl dt").Select(d => d.TextContent));
        Assert.Equal("demo-client", page.One(".dl dd")!.TextContent);
        Assert.Equal("Opis problemu (klient)", page.All("h2").Last().TextContent); // J3: the practitioner reads it
        Assert.Single(page.All("*"), e => e.ChildElementCount == 0 && e.TextContent == "Opłacona"); // J3: getByText("Opłacona") must be one element
    }

    [Fact]
    public async Task A_missing_name_leaves_the_row_out_F03_instead_of_printing_Programista_Programista()
    {
        await using var f = await Open(Detail(practitioner: "  "));
        var page = await f.Browser.GetAsync(Client);
        Assert.DoesNotContain("Programista", page.All(".dl dt").Select(d => d.TextContent));
        await using var g = await Open(Detail(client: ""));
        Assert.DoesNotContain("Klient", (await g.Browser.GetAsync(Detail_ + "?role=practitioner")).All(".dl dt").Select(d => d.TextContent));
    }

    [Theory]
    [InlineData("Refunded", 25000, "Zwrócona w całości", "badge badge--info")]
    [InlineData("PartiallyRefunded", 9000, "Zwrócona częściowo", "badge badge--info")]
    public async Task A_refund_is_a_badge_and_the_amount_as_text_next_to_it_J5_J6(string payment, long refunded, string label, string css)
    {
        await using var f = await Open(Detail("CancelledByClient", payment: payment, refunded: refunded));
        var page = await f.Browser.GetAsync(Client);
        var dd = page.All(".dl dd").Last();
        Assert.Equal(label, dd.QuerySelector(".badge")!.TextContent);
        Assert.Equal(css, dd.QuerySelector(".badge")!.ClassName);
        Assert.Matches(@"Zwrócono: \d+,\d\d zł", dd.TextContent);
        Assert.Contains(refunded == 25000 ? "250,00" : "90,00", dd.TextContent);
        Assert.Equal("badge badge--neutral", page.All(".dl dd .badge").First(b => b.TextContent == "Anulowana przez klienta").ClassName);
    }

    [Fact]
    public async Task A_late_cancellation_keeps_the_paid_line_and_shows_no_refund_J5()
    {
        await using var f = await Open(Detail("LateCancelledByClient"));
        var page = await f.Browser.GetAsync(Client);
        Assert.Equal("Anulowana przez klienta (bez zwrotu)", page.All(".dl dd").First(d => d.TextContent.Contains("Anulowana")).TextContent);
        Assert.Contains("Opłacona", Values(page));
        Assert.DoesNotContain("Zwrócono:", page.Text);
    }

    [Fact]
    public async Task Fee_and_payout_are_shown_when_the_service_sends_them()
    {
        await using var f = await Open(Detail("Completed", fee: new MoneyDto(3750, "PLN"), payout: new MoneyDto(21250, "PLN")));
        var page = await f.Browser.GetAsync(Detail_ + "?role=practitioner");
        Assert.Equal(["Prowizja", "Wypłata dla programisty"], page.All(".dl dt").Select(d => d.TextContent).TakeLast(2));
        Assert.Equal(["37,50 zł", "212,50 zł"], page.All(".dl dd").Select(d => d.TextContent).TakeLast(2));
    }

    [Fact]
    public async Task The_brief_is_text_never_markup()
    {
        await using var f = await Open(Detail(brief: "<script>alert('x')</script> <b>pogrubione</b>"));
        var page = await f.Browser.GetAsync(Client);
        Assert.Empty(page.All("script:not([src])"));
        Assert.Empty(page.All("p.brief b"));
        Assert.Equal("<script>alert('x')</script> <b>pogrubione</b>", page.One("p.brief")!.TextContent);
    }

    [Fact]
    public async Task A_state_the_page_does_not_know_is_shown_as_unknown_never_as_the_raw_value()
    {
        await using var f = await Open(Detail("SomethingNew", payment: "Weird"));
        var page = await f.Browser.GetAsync(Client);
        Assert.Contains("Status nieznany", page.Text);
        Assert.Contains("Stan płatności nieznany", page.Text);
        Assert.DoesNotContain("SomethingNew", page.Html);
    }

    // ---- failures

    [Fact]
    public async Task An_unknown_booking_and_somebody_elses_are_the_same_404_and_a_visitor_goes_to_sign_in()
    {
        await using var f = await Open();
        f.Web.Booking.Respond = _ => FakeBackend.Problem(HttpStatusCode.NotFound, "not_found");
        Assert.Equal(HttpStatusCode.NotFound, (await f.Browser.GetAsync(Client)).Status);
        f.Web.Booking.Respond = _ => FakeBackend.Problem(HttpStatusCode.Forbidden, "forbidden");
        Assert.Equal(HttpStatusCode.NotFound, (await f.Browser.GetAsync(Client)).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await f.Browser.GetAsync("/moje/rezerwacje/nie-guid")).Status);
        await using var visitor = await ClientAreaFixture.Visitor();
        Assert.Equal($"/logowanie?redirect=%2Fmoje%2Frezerwacje%2F{BookingId}%3Frole%3Dclient", (await visitor.Browser.GetAsync(Client)).Location);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, "dependency_unavailable", "Usługa rezerwacji jest chwilowo niedostępna. Spróbuj ponownie za chwilę.")]
    [InlineData(HttpStatusCode.TooManyRequests, "rate_limited", "Zbyt wiele prób. Odczekaj chwilę i spróbuj ponownie.")]
    public async Task A_service_that_cannot_answer_is_an_alert_with_a_reload_and_the_title_stays(HttpStatusCode status, string code, string text)
    {
        await using var f = await Open();
        f.Web.Booking.Respond = _ => FakeBackend.Problem(status, code);
        var page = await f.Browser.GetAsync(Client);
        var alert = page.One("[data-testid=unavailable]")!;
        Assert.Equal("alert", alert.GetAttribute("role"));
        Assert.Equal(text, alert.QuerySelector("p")!.TextContent);
        Assert.Equal(Client, alert.QuerySelector("a")!.GetAttribute("href"));
        Assert.Equal("Szczegóły rezerwacji", page.TextOf("h1"));
    }

    // ---- actions are driven by the server's flags only

    [Theory]
    [InlineData(false, false, false, new string[0])]
    [InlineData(true, false, false, new[] { "Anuluj rezerwację" })]
    [InlineData(false, true, false, new[] { "Zgłoś spór" })]
    [InlineData(false, false, true, new[] { "Zgłoś nieobecność klienta" })]
    [InlineData(true, true, true, new[] { "Zgłoś spór", "Zgłoś nieobecność klienta", "Anuluj rezerwację" })] // the destructive one is last
    public async Task Exactly_the_actions_the_service_allows_are_buttons(bool cancel, bool dispute, bool noShow, string[] names)
    {
        await using var f = await Open(Detail(cancel: cancel, dispute: dispute, noShow: noShow));
        var page = await f.Browser.GetAsync(Client);
        Assert.Equal(names, page.All("main form[method=get] button").Select(b => b.TextContent));
        Assert.Equal(names.Length > 0 ? ["client"] : [], page.All("main form[method=get] input[name=role]").Select(i => i.GetAttribute("value")!));
    }

    [Fact]
    public async Task A_state_with_no_flags_offers_no_action_even_if_the_url_asks_for_one()
    {
        await using var f = await Open(Detail("Completed"));
        foreach (var action in new[] { "cancel", "dispute", "noshow" })
        {
            var page = await f.Browser.GetAsync(Client + "&action=" + action);
            Assert.Empty(page.All("dialog"));
            Assert.Empty(page.All("form[method=post]"));
        }
    }

    [Fact]
    public async Task A_reviewable_booking_offers_the_review_as_the_primary_action()
    {
        await using var f = await Open(Detail("Completed", review: true));
        var link = (await f.Browser.GetAsync(Client)).One(".form-actions a.btn--primary")!;
        Assert.Equal("Wystaw opinię", link.TextContent);
        Assert.Equal($"/moje/opinie/{BookingId}", link.GetAttribute("href"));
    }

    [Fact]
    public async Task The_cancel_trigger_is_a_button_and_opens_an_alertdialog_in_the_markup_J5()
    {
        await using var f = await Open(Detail(cancel: true));
        var button = (await f.Browser.GetAsync(Client)).One("main form[method=get] button")!;
        Assert.Equal(("action", "cancel", "btn btn--ghost-danger"), (button.GetAttribute("name")!, button.GetAttribute("value")!, button.ClassName));

        var page = await f.Browser.GetAsync(Client + "&action=cancel");
        var dialog = page.One("dialog")!;
        Assert.Equal("alertdialog", dialog.GetAttribute("role"));
        Assert.True(dialog.HasAttribute("open"));
        Assert.Equal("Czy na pewno anulować rezerwację?", page.TextOf("#" + dialog.GetAttribute("aria-labelledby")));
        Assert.Contains("co najmniej 24 godziny, zwrot wynosi 100% ceny", dialog.TextContent); // J5, until the service sends the outcome of this booking
        Assert.Contains("mniej niż 24 godziny, brak zwrotu", dialog.TextContent);
        Assert.Equal(["Tak, anuluj rezerwację"], dialog.QuerySelectorAll("button").Select(b => b.TextContent));
        Assert.Equal("btn btn--danger", dialog.QuerySelector("button")!.ClassName); // the danger button lives only in the confirmation
        Assert.Equal("Nie anuluj", dialog.QuerySelector("a")!.TextContent);
        Assert.Equal(Client, dialog.QuerySelector("a")!.GetAttribute("href"));
        Assert.Empty(page.All("main form[method=get]")); // the trigger is gone while the dialog is open: getByRole("button", "Anuluj rezerwację") is not ambiguous
        Assert.Equal("Powód (opcjonalnie)", page.One("label[for=reason]")!.TextContent.Trim());
    }

    [Fact]
    public async Task When_the_service_sends_the_outcome_of_this_booking_the_dialog_says_it_and_does_not_repeat_the_rule_F11()
    {
        await using var full = await Open(Detail(cancel: true, outcome: new CancelOutcomeDto("full_refund", new MoneyDto(25000, "PLN"))));
        var dialog = (await full.Browser.GetAsync(Client + "&action=cancel")).One("dialog")!;
        Assert.Contains("Otrzymasz zwrot 250,00 zł.", dialog.TextContent);
        Assert.DoesNotContain("co najmniej 24 godziny", dialog.TextContent); // UX-031: the consequence once, not the rule again

        await using var none = await Open(Detail(cancel: true, outcome: new CancelOutcomeDto("no_refund", new MoneyDto(0, "PLN"))));
        var late = (await none.Browser.GetAsync(Client + "&action=cancel")).One("dialog")!;
        Assert.Contains("nie otrzymasz zwrotu", late.TextContent);
        Assert.DoesNotContain("Otrzymasz zwrot 250", late.TextContent);
    }

    [Fact]
    public async Task Without_the_field_the_dialog_says_only_the_general_rule()
    {
        await using var f = await Open(Detail(cancel: true, outcome: null));
        var dialog = (await f.Browser.GetAsync(Client + "&action=cancel")).One("dialog")!;
        Assert.Contains("Zwrot zależy od terminu", dialog.TextContent);
        Assert.DoesNotContain(dialog.QuerySelectorAll("strong"), s => s.TextContent.StartsWith("Otrzymasz"));
    }

    [Fact]
    public async Task A_practitioner_is_told_the_client_is_refunded_in_full_and_they_are_not_paid()
    {
        await using var f = await Open(Detail(cancel: true));
        var dialog = (await f.Browser.GetAsync(Detail_ + "?role=practitioner&action=cancel")).One("dialog")!;
        Assert.Contains("Jako programista anulujesz zawsze z pełnym zwrotem", dialog.TextContent);
    }

    // ---- cancel

    [Fact]
    public async Task Confirming_the_cancel_sends_the_reason_and_redirects_to_the_page_with_a_fixed_key_J5()
    {
        await using var f = await Open(Detail(cancel: true), post: _ => FakeBackend.Json(Detail("CancelledByClient", payment: "Refunded", refunded: 25000)));
        var page = await f.Post(Client + "&action=cancel", "cancel", ("Model.Reason", "  Zmiana planów.  "));
        Assert.Equal(HttpStatusCode.Found, page.Status);
        Assert.Equal($"/moje/rezerwacje/{BookingId}?role=client&done=cancelled", page.Location);
        var call = f.Web.Booking.Requests.Single(r => r.Method == HttpMethod.Post);
        Assert.Equal($"/api/v1/bookings/{BookingId}/cancel", call.PathAndQuery);
        Assert.Equal("Zmiana planów.", JsonDocument.Parse(call.Body!).RootElement.GetProperty("reason").GetString());
        Assert.StartsWith("Bearer eyJ", call.Authorization);
        Assert.DoesNotContain("Zmiana", page.Location); // the reason is never in a URL
    }

    [Fact]
    public async Task Cancelling_without_a_reason_sends_an_empty_object()
    {
        await using var f = await Open(Detail(cancel: true), post: _ => FakeBackend.Json(Detail("CancelledByClient")));
        await f.Post(Client + "&action=cancel", "cancel", ("Model.Reason", "   "));
        Assert.Equal("{}", f.Web.Booking.Requests.Single(r => r.Method == HttpMethod.Post).Body);
    }

    [Theory]
    [InlineData("cancelled", "Rezerwacja została anulowana.")]
    [InlineData("disputed", "Spór został zgłoszony. Wypłata dla programisty jest wstrzymana do rozstrzygnięcia.")]
    [InlineData("noshow", "Nieobecność klienta została zgłoszona.")]
    public async Task The_result_of_an_action_is_a_status_message_chosen_by_a_fixed_key_J5_J6(string key, string text)
    {
        await using var f = await Open(Detail("CancelledByClient"));
        var page = await f.Browser.GetAsync(Client + "&done=" + key);
        Assert.Equal(text, page.One("[data-testid=done]")!.TextContent);
        Assert.Equal("status", page.One("[data-testid=done]")!.GetAttribute("role"));
        Assert.Single(page.All("*"), e => e.ChildElementCount == 0 && e.TextContent.Contains("Rezerwacja została anulowana.") == (key == "cancelled") && e.TextContent == text); // exactly one element says it
    }

    [Theory]
    [InlineData("<b>x</b>")] [InlineData("Rezerwacja została anulowana.")] [InlineData("")] [InlineData("javascript:alert(1)")]
    public async Task A_message_key_that_is_not_one_of_ours_shows_nothing_and_text_from_the_query_is_never_rendered(string key)
    {
        await using var f = await Open();
        var page = await f.Browser.GetAsync(Client + "&done=" + Uri.EscapeDataString(key));
        Assert.Empty(page.All("[data-testid=done]"));
        if (key.Length > 0) Assert.DoesNotContain(key, page.Html.Replace("&amp;", "&"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_reason_over_500_characters_is_refused_inline_before_the_service_is_asked()
    {
        await using var f = await Open(Detail(cancel: true));
        var page = await f.Post(Client + "&action=cancel", "cancel", ("Model.Reason", new string('a', 501)));
        Assert.Equal("Powód może mieć najwyżej 500 znaków.", page.TextOf(".form-summary a"));
        Assert.Equal("true", page.One("#reason")!.GetAttribute("aria-invalid"));
        Assert.DoesNotContain(f.Web.Booking.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task A_state_that_changed_under_the_user_says_so_and_offers_the_refresh()
    {
        await using var f = await Open(Detail(cancel: true), post: _ => FakeBackend.Problem(HttpStatusCode.Conflict, "invalid_transition"));
        var page = await f.Post(Client + "&action=cancel", "cancel", ("Model.Reason", ""));
        var alert = page.One("[data-testid=action-error]")!;
        Assert.Equal("alert", alert.GetAttribute("role"));
        Assert.Equal("To nie jest już możliwe: stan rezerwacji się zmienił. Odśwież stronę.", alert.QuerySelector("p")!.TextContent);
        Assert.Equal(("Odśwież", Client), (alert.QuerySelector("a")!.TextContent, alert.QuerySelector("a")!.GetAttribute("href")!));
        Assert.NotNull(page.One("dialog")); // the dialog stays so the user sees why
    }

    [Fact]
    public async Task A_401_during_an_action_goes_to_sign_in_and_back_to_the_booking()
    {
        await using var f = await Open(Detail(cancel: true), post: _ => FakeBackend.Problem(HttpStatusCode.Unauthorized, "unauthenticated"));
        var page = await f.Post(Client + "&action=cancel", "cancel");
        Assert.Equal(HttpStatusCode.Found, page.Status);
        Assert.StartsWith("/logowanie?redirect=%2Fmoje%2Frezerwacje%2F", page.Location);
    }

    // ---- dispute (J6)

    [Fact]
    public async Task The_dispute_form_has_the_labels_and_buttons_the_journey_uses()
    {
        await using var f = await Open(Detail(dispute: true));
        var page = await f.Browser.GetAsync(Client + "&action=dispute");
        Assert.Equal("Rodzaj sporu", page.One("label[for=kind]")!.TextContent);
        Assert.Equal(["Konsultacja różniła się od opisu", "Programista się nie stawił", "Inny powód"], page.All("#kind option").Select(o => o.TextContent));
        Assert.Equal("Opis sporu", page.One("label[for=text]")!.TextContent);
        Assert.Equal(["Wyślij zgłoszenie"], page.All("dialog form button").Select(b => b.TextContent));
        Assert.Single(page.All("label"), l => l.TextContent.Contains("opis sporu", StringComparison.OrdinalIgnoreCase)); // getByLabel is a substring match: one control
        Assert.Equal("0 / 1000", page.TextOf(".field__counter"));
    }

    [Fact]
    public async Task A_valid_dispute_sends_the_kind_and_the_text_and_redirects_with_a_fixed_key()
    {
        await using var f = await Open(Detail(dispute: true), post: _ => FakeBackend.Json(Detail("Disputed", payment: "Succeeded")));
        var page = await f.Post(Client + "&action=dispute", "dispute", ("Model.Kind", "practitioner_no_show"), ("Model.Text", "Programista się nie połączył, dane syntetyczne testu."));
        Assert.Equal($"/moje/rezerwacje/{BookingId}?role=client&done=disputed", page.Location);
        var call = f.Web.Booking.Requests.Single(r => r.Method == HttpMethod.Post);
        Assert.Equal($"/api/v1/bookings/{BookingId}/dispute", call.PathAndQuery);
        var body = JsonDocument.Parse(call.Body!).RootElement;
        Assert.Equal("practitioner_no_show", body.GetProperty("kind").GetString());
        Assert.Equal("Programista się nie połączył, dane syntetyczne testu.", body.GetProperty("text").GetString());
    }

    [Theory]
    [InlineData("krótko", "Opis musi mieć co najmniej 10 znaków.")]
    public async Task A_short_dispute_is_one_summary_and_an_inline_error_and_the_service_is_not_asked(string text, string message)
    {
        await using var f = await Open(Detail(dispute: true));
        var page = await f.Post(Client + "&action=dispute", "dispute", ("Model.Kind", "other"), ("Model.Text", text));
        Assert.Equal("Popraw 1 pole", page.TextOf(".form-summary h2"));
        Assert.Equal(message, page.TextOf(".form-summary a"));
        Assert.Equal("#text", page.One(".form-summary a")!.GetAttribute("href"));
        Assert.Equal("Błąd: " + message, Page.Squash(page.TextOf("#text-error")));
        Assert.Equal(text, page.One("#text")!.TextContent);
        Assert.Equal("6 / 1000", page.TextOf(".field__counter"));
        Assert.DoesNotContain(f.Web.Booking.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task An_unknown_kind_is_refused_with_the_summary_and_the_selected_option_does_not_come_from_the_attacker()
    {
        await using var f = await Open(Detail(dispute: true));
        var page = await f.Post(Client + "&action=dispute", "dispute", ("Model.Kind", "fraud"), ("Model.Text", "Wystarczająco długi opis sporu."));
        Assert.Equal("Wybierz rodzaj sporu.", page.TextOf(".form-summary a"));
        Assert.DoesNotContain(f.Web.Booking.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task A_field_the_service_names_is_shown_under_that_field_in_our_words_never_its_message()
    {
        await using var f = await Open(Detail(dispute: true), post: _ => FakeBackend.Raw("""{"code":"validation_failed","status":422,"errors":{"text":["The field Text must be a string with a minimum length of 10."]}}""", HttpStatusCode.UnprocessableEntity));
        var page = await f.Post(Client + "&action=dispute", "dispute", ("Model.Kind", "other"), ("Model.Text", "Wystarczająco długi opis sporu."));
        Assert.Equal("Opis musi mieć co najmniej 10 znaków.", page.TextOf(".form-summary a"));
        Assert.DoesNotContain("must be a string", page.Html);
        Assert.Equal("Sprawdź wypełnione pola i spróbuj ponownie.", page.TextOf("[data-testid=action-error] p"));
    }

    [Fact]
    public async Task The_dispute_text_is_never_written_to_a_log_or_a_url()
    {
        const string text = "Znacznik sporu opis syntetyczny";
        await using var f = await Open(Detail(dispute: true, cancel: true), post: _ => FakeBackend.Problem(HttpStatusCode.InternalServerError, "boom"));
        var page = await f.Post(Client + "&action=dispute", "dispute", ("Model.Kind", "other"), ("Model.Text", text));
        await f.Post(Client + "&action=cancel", "cancel", ("Model.Reason", text));
        Assert.DoesNotContain(f.Logs.Lines, l => l.Contains("Znacznik sporu", StringComparison.Ordinal));
        Assert.DoesNotContain(f.Web.Booking.Requests, r => r.PathAndQuery.Contains("Znacznik"));
        Assert.Null(page.Location);
    }

    // ---- no-show (practitioner)

    [Fact]
    public async Task A_practitioner_can_report_a_no_show_after_a_confirmation_and_it_posts_to_the_endpoint()
    {
        await using var f = await Open(Detail(noShow: true), post: _ => FakeBackend.Json(Detail("ClientNoShow")));
        var dialog = (await f.Browser.GetAsync(Detail_ + "?role=practitioner&action=noshow")).One("dialog")!;
        Assert.Equal("alertdialog", dialog.GetAttribute("role"));
        Assert.Contains("Tego nie można cofnąć.", dialog.TextContent);
        Assert.Equal(["Tak, zgłoś nieobecność"], dialog.QuerySelectorAll("button").Select(b => b.TextContent));
        var page = await f.Post(Detail_ + "?role=practitioner&action=noshow", "noshow");
        Assert.Equal($"/moje/rezerwacje/{BookingId}?role=practitioner&done=noshow", page.Location);
        Assert.Equal($"/api/v1/bookings/{BookingId}/report-client-no-show", f.Web.Booking.Requests.Single(r => r.Method == HttpMethod.Post).PathAndQuery);
    }

    [Fact]
    public async Task Without_the_antiforgery_token_no_action_reaches_the_service()
    {
        await using var f = await Open(Detail(cancel: true));
        var request = new HttpRequestMessage(HttpMethod.Post, Client + "&action=cancel") { Content = new FormUrlEncodedContent([new("_handler", "cancel"), new("Model.Reason", "x")]) };
        var page = await f.Browser.SendAsync(request);
        Assert.NotEqual(HttpStatusCode.Found, page.Status);
        Assert.DoesNotContain(f.Web.Booking.Requests, r => r.Method == HttpMethod.Post);
    }
}
