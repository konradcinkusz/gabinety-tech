using System.Net;
using System.Text.Json;
using Gabinety.Web.Api;
using static Gabinety.Web.Tests.Pages.ClientAreaFixture;

namespace Gabinety.Web.Tests.Pages;

/// <summary>The mock checkout (fake profile only, fail closed), the confirmation, and the page the payment provider returns to.</summary>
public class CheckoutAndConfirmationTests
{
    static readonly string Checkout = $"/checkout/mock/{BookingId}";
    static readonly string Confirmation = $"/rezerwacja/potwierdzenie/{BookingId}";

    static async Task<ClientAreaFixture> Fake(BookingDetailDto? booking = null, Action<WebFactory>? configure = null)
    {
        var f = await ClientAreaFixture.SignedIn(w => { w.With("GABINETY_FAKE", "1"); configure?.Invoke(w); });
        f.Web.Booking.Respond = r => r.RequestUri!.AbsolutePath.EndsWith("/payments/simulate") ? FakeBackend.Json(new { outcome = "PaymentSucceeded" }) : FakeBackend.Json(booking ?? Pending());
        return f;
    }

    static BookingDetailDto Pending(DateTimeOffset? hold = null) => Detail("PendingPayment", payment: "Created", hold: hold ?? DateTimeOffset.UtcNow.AddMinutes(9), brief: null);

    // ---- the mock exists only where the backend would accept it

    [Fact]
    public async Task Outside_the_fake_profile_the_checkout_is_a_404_and_no_backend_is_asked()
    {
        await using var f = await ClientAreaFixture.SignedIn(); // no GABINETY_FAKE, the Development ASP.NET environment is not the fake profile
        var page = await f.Browser.GetAsync(Checkout);
        Assert.Equal(HttpStatusCode.NotFound, page.Status);
        Assert.Empty(f.Web.Booking.Requests);
        Assert.DoesNotContain("Symuluj", page.Html);
    }

    [Theory]
    [InlineData("GABINETY_FAKE", "1", null, true)]
    [InlineData("Environment:Kind", "Development", null, true)]
    [InlineData("Environment:Kind", "development", "Mock", true)]
    [InlineData("GABINETY_FAKE", "1", "mock", true)]
    [InlineData("GABINETY_FAKE", "1", "Stripe", false)] // a real provider turns the mock off whatever the profile says
    [InlineData("GABINETY_FAKE", "1", "Whatever", false)] // unknown means no
    [InlineData("GABINETY_FAKE", "0", null, false)]
    [InlineData("Environment:Kind", "Production", null, false)]
    [InlineData("Environment:Kind", "Staging", "Mock", false)]
    public async Task The_checkout_exists_exactly_when_the_booking_service_would_accept_the_simulated_payment(string key, string value, string? provider, bool exists)
    {
        await using var f = await ClientAreaFixture.SignedIn(w => { w.With(key, value); if (provider is not null) w.With("Payments:Provider", provider); });
        f.Web.Booking.Respond = _ => FakeBackend.Json(Pending());
        Assert.Equal(exists ? HttpStatusCode.OK : HttpStatusCode.NotFound, (await f.Browser.GetAsync(Checkout)).Status);
    }

    [Fact]
    public async Task A_visitor_is_sent_to_sign_in()
    {
        await using var f = await ClientAreaFixture.Visitor(w => w.With("GABINETY_FAKE", "1"));
        var page = await f.Browser.GetAsync(Checkout);
        Assert.Equal(HttpStatusCode.Found, page.Status);
        Assert.Equal($"/logowanie?redirect=%2Fcheckout%2Fmock%2F{BookingId}", page.Location);
    }

    // ---- the page

    [Fact]
    public async Task The_checkout_says_it_is_a_demonstration_and_shows_the_summary_with_the_name_the_timer_and_two_outcomes()
    {
        await using var f = await Fake();
        var page = await f.Browser.GetAsync(Checkout);
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal(["Płatność (tryb demonstracyjny)"], page.All("h1").Select(h => h.TextContent));
        Assert.Contains("tryb demonstracyjny", page.One(".callout--info")!.TextContent);
        var summary = page.One(".booking-summary")!;
        Assert.Equal(["Audyt kodu demo", "Anna Demowska", "wtorek, 8 grudnia, 12:00–13:00", "60 min", "250,00\u00a0zł"], summary.QuerySelectorAll("dd").Select(d => d.TextContent));
        var timer = summary.QuerySelector("[role=timer]")!;
        Assert.Contains("Termin jest wstrzymany jeszcze przez", timer.TextContent);
        Assert.Matches(@"\d:\d\d", timer.TextContent);
        Assert.Contains("callout--warning", timer.ClassName);
        Assert.Equal(["Symuluj udaną płatność", "Symuluj nieudaną płatność"], page.All("form button").Select(b => b.TextContent));
        Assert.Equal(["btn btn--primary", "btn btn--ghost"], page.All("form button").Select(b => b.ClassName)); // the failure is the quiet one
        Assert.Empty(page.All("form button[disabled]"));
        Assert.NotNull(page.One("form input[name=__RequestVerificationToken]"));
    }

    [Fact]
    public async Task Only_the_clock_is_interactive_the_two_buttons_are_plain_form_posts_that_no_late_circuit_can_lose()
    {
        await using var f = await Fake();
        var page = await f.Browser.GetAsync(Checkout);
        Assert.Contains("<!--Blazor:{", page.Html); // one interactive root component: the timer
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(page.Html, "\"type\":\"server\""));
        Assert.Empty(page.One("form")!.QuerySelectorAll("[blazor\\:onclick]"));
        Assert.Equal("post", page.One("form")!.GetAttribute("method"));
    }

    [Fact]
    public async Task A_booking_that_is_no_longer_pending_says_so_and_confirmed_leads_to_the_booking_else_to_the_profile()
    {
        await using var confirmed = await Fake(Detail("Confirmed"));
        var page = await confirmed.Browser.GetAsync(Checkout);
        Assert.Equal("Ta rezerwacja nie oczekuje już na płatność.", page.TextOf(".callout p"));
        Assert.Equal($"/rezerwacja/potwierdzenie/{BookingId}", page.One(".callout a")!.GetAttribute("href"));
        Assert.Empty(page.All("form"));

        await using var expired = await Fake(Detail("Expired", payment: "Failed"));
        var other = await expired.Browser.GetAsync(Checkout);
        Assert.Equal("/gabinet/demo-anna-demowska", other.One(".callout a")!.GetAttribute("href"));
    }

    [Fact]
    public async Task When_the_hold_has_run_out_the_page_says_so_with_the_way_back_and_the_buttons_are_off()
    {
        await using var f = await Fake(Pending(DateTimeOffset.UtcNow.AddMinutes(-1)));
        var page = await f.Browser.GetAsync(Checkout);
        Assert.Contains("Czas na płatność minął, termin został zwolniony.", page.Text);
        Assert.All(page.All("form button"), b => Assert.True(b.HasAttribute("disabled"))); // never an opacity trick: really disabled
        Assert.Contains(page.All("a"), a => a.GetAttribute("href") == "/gabinet/demo-anna-demowska");
    }

    [Fact]
    public async Task A_booking_that_cannot_be_read_says_refresh_the_page_with_the_link_that_does_it()
    {
        await using var f = await Fake();
        f.Web.Booking.Respond = _ => FakeBackend.Problem(HttpStatusCode.TooManyRequests, "rate_limited");
        var page = await f.Browser.GetAsync(Checkout);
        var alert = page.One("[data-testid=unavailable]")!;
        Assert.Equal("alert", alert.GetAttribute("role"));
        Assert.Contains("Odśwież stronę", alert.TextContent); // J3: the harness reloads on this text
        Assert.Equal(Checkout, alert.QuerySelector("a")!.GetAttribute("href"));
        Assert.Empty(page.All("form"));
    }

    [Theory]
    [InlineData(404)] [InlineData(403)]
    public async Task Another_users_booking_is_a_404_not_a_leak(int status)
    {
        await using var f = await Fake();
        f.Web.Booking.Respond = _ => FakeBackend.Problem((HttpStatusCode)status, status == 404 ? "not_found" : "forbidden");
        Assert.Equal(HttpStatusCode.NotFound, (await f.Browser.GetAsync(Checkout)).Status);
    }

    // ---- the outcomes

    [Fact]
    public async Task A_successful_payment_is_simulated_with_the_booking_id_and_the_browser_lands_on_the_confirmation()
    {
        await using var f = await Fake();
        var page = await f.Post(Checkout, "checkout", ("Model.Outcome", "succeeded"));
        Assert.Equal(HttpStatusCode.Found, page.Status);
        Assert.Equal(Confirmation, page.Location);
        var call = f.Web.Booking.Requests.Single(r => r.Method == HttpMethod.Post);
        Assert.Equal("/api/v1/payments/simulate", call.PathAndQuery);
        Assert.StartsWith("Bearer eyJ", call.Authorization);
        using var body = JsonDocument.Parse(call.Body!);
        Assert.Equal(BookingId.ToString(), body.RootElement.GetProperty("bookingId").GetString());
        Assert.Equal("succeeded", body.RootElement.GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task A_failed_payment_stays_on_the_page_says_so_and_offers_the_profile_and_nothing_more_to_simulate()
    {
        await using var f = await Fake();
        var page = await f.Post(Checkout, "checkout", ("Model.Outcome", "failed"));
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal("failed", JsonDocument.Parse(f.Web.Booking.Requests.Single(r => r.Method == HttpMethod.Post).Body!).RootElement.GetProperty("outcome").GetString());
        var alert = page.One(".callout--danger[role=alert]")!;
        Assert.Contains("Płatność nie powiodła się. Rezerwacja nie została potwierdzona.", alert.TextContent);
        Assert.Equal("/gabinet/demo-anna-demowska", alert.QuerySelector("a")!.GetAttribute("href"));
        Assert.All(page.All("form button"), b => Assert.True(b.HasAttribute("disabled")));
    }

    [Theory]
    [InlineData(403, "Symulacja płatności jest wyłączona w tym środowisku.")]
    [InlineData(404, "Symulacja płatności jest wyłączona w tym środowisku.")]
    [InlineData(500, "Symulacja nie powiodła się. Spróbuj ponownie.")]
    public async Task A_refused_simulation_is_an_alert_in_our_words(int status, string text)
    {
        await using var f = await Fake();
        f.Web.Booking.Respond = r => r.Method == HttpMethod.Post ? FakeBackend.Problem((HttpStatusCode)status, "forbidden") : FakeBackend.Json(Pending());
        var page = await f.Post(Checkout, "checkout", ("Model.Outcome", "succeeded"));
        Assert.Equal(text, page.One(".callout--danger[role=alert]")!.TextContent);
        Assert.DoesNotContain("SERVER-TITLE-MUST-NOT-APPEAR", page.Html);
    }

    [Theory]
    [InlineData("")] [InlineData("refund")] [InlineData("<script>")]
    public async Task An_outcome_that_is_not_one_of_the_two_is_ignored_and_nothing_is_sent(string outcome)
    {
        await using var f = await Fake();
        await f.Post(Checkout, "checkout", ("Model.Outcome", outcome));
        Assert.DoesNotContain(f.Web.Booking.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task A_post_to_the_checkout_outside_the_fake_profile_is_a_404_and_simulates_nothing()
    {
        await using var f = await ClientAreaFixture.SignedIn();
        f.Web.Booking.Respond = _ => FakeBackend.Json(Pending());
        var request = new HttpRequestMessage(HttpMethod.Post, Checkout) { Content = new FormUrlEncodedContent([new("_handler", "checkout"), new("Model.Outcome", "succeeded")]) };
        var page = await f.Browser.SendAsync(request);
        Assert.NotEqual(HttpStatusCode.Found, page.Status);
        Assert.Empty(f.Web.Booking.Requests);
    }

    // ---- confirmation

    static Task<ClientAreaFixture> Open(BookingDetailDto booking) => ClientAreaFixture.SignedIn(w => { }).ContinueWith(t => { t.Result.Web.Booking.Respond = _ => FakeBackend.Json(booking); return t.Result; });

    [Fact]
    public async Task A_confirmed_booking_says_so_in_the_exact_words_with_the_summary_the_name_and_two_ways_on()
    {
        await using var f = await Open(Detail("Confirmed", meeting: "https://meet.example/demo-sala"));
        var page = await f.Browser.GetAsync(Confirmation);
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal("Rezerwacja potwierdzona. Dziękujemy!", page.One(".callout--success")!.TextContent); // J3: getByText must find exactly one
        Assert.Single(page.All("*"), e => e.ChildElementCount == 0 && e.TextContent == "Rezerwacja potwierdzona. Dziękujemy!");
        var dd = page.QuerySelectorAllText(".booking-summary dd");
        Assert.Equal(["Audyt kodu demo", "Anna Demowska", "wtorek, 8 grudnia, 12:00–13:00", "60 min", "250,00\u00a0zł"], dd);
        Assert.DoesNotContain(" ·", page.TextOf(".booking-summary")); // no separator glued to nothing (F-03)
        // UX-021: three ways on (the calendar file is added between the detail and the search)
        Assert.Equal([$"/moje/rezerwacje/{BookingId}?role=client", $"/moje/rezerwacje/{BookingId}/kalendarz.ics", "/programisci"], page.All(".form-actions a").Select(a => a.GetAttribute("href")));
        Assert.Equal(["btn btn--primary", "btn btn--secondary", "btn btn--secondary"], page.All(".form-actions a").Select(a => a.ClassName));
        Assert.Equal(["Zobacz rezerwację", "Dodaj do kalendarza", "Wróć do wyszukiwarki"], page.All(".form-actions a").Select(a => a.TextContent.Trim()));
        var meeting = page.One("a[target=_blank]")!;
        Assert.Equal("https://meet.example/demo-sala", meeting.GetAttribute("href"));
        Assert.Equal("noopener noreferrer", meeting.GetAttribute("rel"));
        Assert.Contains("Otwórz go w terminie konsultacji.", page.Text); // the link is explained
        Assert.Empty(page.All("meta[http-equiv=refresh]"));
    }

    [Fact]
    public async Task Without_a_name_in_the_booking_the_summary_leaves_the_row_out_instead_of_printing_a_placeholder_F03()
    {
        await using var f = await Open(Detail("Confirmed", practitioner: ""));
        var page = await f.Browser.GetAsync(Confirmation);
        Assert.Equal(["Konsultacja", "Termin", "Czas trwania", "Do zapłaty"], page.All(".booking-summary dt").Select(d => d.TextContent));
    }

    [Theory]
    [InlineData("javascript:alert(1)")] [InlineData("data:text/html,x")] [InlineData("/wzgledny")] [InlineData("ftp://x.example/a")]
    public async Task A_meeting_link_that_is_not_http_or_https_is_not_rendered(string url)
    {
        await using var f = await Open(Detail("Confirmed", meeting: url));
        var page = await f.Browser.GetAsync(Confirmation);
        Assert.Empty(page.All("a[target=_blank]"));
        Assert.DoesNotContain(url, page.Html);
    }

    [Fact]
    public async Task A_meeting_link_of_a_booking_that_is_not_confirmed_is_not_shown_even_when_the_service_sent_one()
    {
        await using var f = await Open(Detail("PendingPayment", meeting: "https://meet.example/x", hold: DateTimeOffset.UtcNow.AddMinutes(5)));
        Assert.Empty((await f.Browser.GetAsync(Confirmation)).All("a[target=_blank]"));
    }

    [Fact]
    public async Task A_payment_still_being_confirmed_says_so_and_reloads_itself_while_the_hold_lasts_and_never_claims_success()
    {
        await using var f = await Open(Detail("PendingPayment", payment: "Created", hold: DateTimeOffset.UtcNow.AddMinutes(5)));
        var page = await f.Browser.GetAsync(Confirmation);
        Assert.Contains("Czekamy na potwierdzenie płatności.", page.One(".callout--info")!.TextContent);
        Assert.DoesNotContain("Rezerwacja potwierdzona", page.Text);
        Assert.Equal("5", page.One("meta[http-equiv=refresh]")!.GetAttribute("content"));
        Assert.Equal(Confirmation, page.One(".callout--info a")!.GetAttribute("href"));
    }

    [Fact]
    public async Task A_pending_payment_whose_hold_has_passed_stops_reloading()
    {
        await using var f = await Open(Detail("PendingPayment", payment: "Created", hold: DateTimeOffset.UtcNow.AddMinutes(-1)));
        Assert.Empty((await f.Browser.GetAsync(Confirmation)).All("meta[http-equiv=refresh]"));
    }

    [Fact]
    public async Task Any_other_state_is_a_warning_not_a_success()
    {
        await using var f = await Open(Detail("Expired", payment: "Failed"));
        var page = await f.Browser.GetAsync(Confirmation);
        Assert.Equal("Ta rezerwacja nie jest aktywna.", page.One(".callout--warning")!.TextContent);
        Assert.DoesNotContain("potwierdzona", page.Text);
    }

    [Fact]
    public async Task A_confirmation_that_cannot_be_read_says_refresh_the_page_J3()
    {
        await using var f = await Open(Detail());
        f.Web.Booking.Respond = _ => FakeBackend.Problem(HttpStatusCode.ServiceUnavailable, "dependency_unavailable");
        var page = await f.Browser.GetAsync(Confirmation);
        Assert.Contains("Odśwież stronę", page.One("[data-testid=unavailable]")!.TextContent);
        Assert.Equal("alert", page.One("[data-testid=unavailable]")!.GetAttribute("role"));
        Assert.Equal(Confirmation, page.One("[data-testid=unavailable] a")!.GetAttribute("href"));
    }

    [Fact]
    public async Task The_confirmation_of_an_unknown_booking_is_a_404_and_a_visitor_goes_to_sign_in()
    {
        await using var f = await Open(Detail());
        f.Web.Booking.Respond = _ => FakeBackend.Problem(HttpStatusCode.NotFound, "not_found");
        Assert.Equal(HttpStatusCode.NotFound, (await f.Browser.GetAsync(Confirmation)).Status);
        await using var visitor = await ClientAreaFixture.Visitor();
        Assert.Equal($"/logowanie?redirect=%2Frezerwacja%2Fpotwierdzenie%2F{BookingId}", (await visitor.Browser.GetAsync(Confirmation)).Location);
    }

    // ---- where the payment provider returns the browser (Stripe Checkout success URL)

    [Fact]
    public async Task The_return_page_of_the_provider_leads_to_the_confirmation_and_needs_a_session()
    {
        await using var f = await Open(Detail());
        var page = await f.Browser.GetAsync($"/bookings/{BookingId}");
        Assert.Equal(HttpStatusCode.Found, page.Status);
        Assert.Equal(Confirmation, page.Location);
        await using var visitor = await ClientAreaFixture.Visitor();
        Assert.StartsWith("/logowanie?redirect=", (await visitor.Browser.GetAsync($"/bookings/{BookingId}")).Location);
    }

    // ---- UX-021: the steps, the headings, "Co dalej", the notice at the width of the summary

    [Fact]
    public async Task The_confirmed_booking_is_titled_as_confirmed_and_any_other_state_keeps_the_plain_title()
    {
        await using var confirmed = await Open(Detail("Confirmed"));
        Assert.Equal(["Rezerwacja potwierdzona"], (await confirmed.Browser.GetAsync(Confirmation)).All("h1").Select(h => h.TextContent));

        await using var pending = await Open(Detail("PendingPayment", payment: "Created", hold: DateTimeOffset.UtcNow.AddMinutes(5)));
        var page = await pending.Browser.GetAsync(Confirmation);
        Assert.Equal(["Rezerwacja"], page.All("h1").Select(h => h.TextContent)); // no page claims success it does not have
        Assert.Empty(page.All(".booking-confirmation__next")); // "Co dalej" describes a confirmed booking only
    }

    [Fact]
    public async Task The_confirmation_says_what_happens_next_under_the_summary_and_the_refund_is_a_link_to_the_price_list_not_a_number()
    {
        await using var f = await Open(Detail("Confirmed"));
        var page = await f.Browser.GetAsync(Confirmation);
        var next = page.One(".booking-confirmation__next")!;
        Assert.Equal("Co dalej", next.QuerySelector("h2")!.TextContent);
        Assert.Equal(3, next.QuerySelectorAll("li").Length);
        Assert.Contains("Wyślemy e-mail ze szczegółami rezerwacji.", next.TextContent);
        Assert.Contains("Moje rezerwacje", next.TextContent);
        Assert.Equal("/cennik", next.QuerySelector("li a")!.GetAttribute("href"));
        Assert.Matches(@"^\s*Rezerwację możesz anulować.*Zasady zwrotu znajdziesz w cenniku\.\s*$", next.QuerySelectorAll("li")[2].TextContent.Replace(' ', ' '));
        Assert.DoesNotMatch(@"\d+\s*(dni|godz|h)\b", next.TextContent); // the window is never stated here
        Assert.True(next.NextElementSibling is null || next.NextElementSibling.ClassList.Contains("form-actions")); // under the summary, above the actions
        Assert.Equal("Rezerwacja potwierdzona. Dziękujemy!", page.One(".callout--success")!.TextContent); // the exact success text is still the hook
    }

    [Fact]
    public async Task The_steps_show_the_last_step_as_current_on_the_confirmation_and_the_payment_step_on_the_checkout()
    {
        await using var confirmed = await Open(Detail("Confirmed"));
        var steps = (await confirmed.Browser.GetAsync(Confirmation)).One(".booking-steps")!;
        Assert.Equal("Gotowe", steps.QuerySelector("[aria-current=step] .booking-steps__name")!.FirstChild!.TextContent.Trim());

        await using var f = await Fake();
        var checkout = (await f.Browser.GetAsync(Checkout)).One(".booking-steps")!;
        Assert.Equal("Płatność", checkout.QuerySelector("[aria-current=step] .booking-steps__name")!.FirstChild!.TextContent.Trim());
        Assert.Equal(2, checkout.QuerySelectorAll(".booking-steps__item--done").Length); // Termin and Opis are finished
    }

    [Fact]
    public async Task The_demonstration_notice_and_the_summary_share_one_column_container()
    {
        await using var f = await Fake();
        var page = await f.Browser.GetAsync(Checkout);
        var notice = page.One(".callout--info")!;
        Assert.Equal(page.One(".booking-summary")!.ParentElement, notice.ParentElement);
        Assert.Equal("checkout-layout", notice.ParentElement!.ClassName);
    }
}

static class PageText
{
    public static IReadOnlyList<string> QuerySelectorAllText(this Page page, string css) => page.All(css).Select(e => e.TextContent).ToList();
}
