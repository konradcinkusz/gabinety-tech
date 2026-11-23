using System.Net;

namespace Gabinety.Web.Tests.PractitionerAreaTests;

public class PaymentsHostTests
{
    [Theory]
    [InlineData("none", "Nie rozpoczęto", "Rozpocznij konfigurację płatności")]
    [InlineData("pending", "Wymaga uzupełnienia", "Dokończ konfigurację płatności")]
    public async Task An_account_that_is_not_ready_says_so_states_the_consequence_and_offers_the_one_action(string state, string label, string button)
    {
        var (web, browser, _) = await PractitionerBrowsers.SignedIn(w => { w.HaveProfile("Approved"); w.PaymentState = state; });
        await using var _ = web;
        var page = await browser.GetAsync("/gabinet-programisty/platnosci");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal("Płatności", page.TextOf("h1"));
        Assert.Equal(label, page.TextOf("[data-testid=payments-status]"));
        Assert.Contains("Bez gotowego konta płatności nie możesz przyjmować rezerwacji", page.TextOf("[data-testid=payments-must-be-ready]")); // the clear message
        Assert.Equal(button, page.One("form button[type=submit]")!.TextContent);
        Assert.Null(page.One("[data-testid=payments-link]"));
    }

    [Theory]
    [InlineData("none", "badge badge--neutral")]
    [InlineData("pending", "badge badge--warning")]
    public async Task A_card_with_one_badge_one_primary_action_and_what_the_client_sees_while_not_ready(string state, string badgeClass)
    {
        var (web, browser, _) = await PractitionerBrowsers.SignedIn(w => { w.HaveProfile("Approved"); w.PaymentState = state; });
        await using var _ = web;
        var page = await browser.GetAsync("/gabinet-programisty/platnosci");
        var card = page.One("section.card")!;
        Assert.Single(card.QuerySelectorAll(".badge"));
        Assert.Equal(badgeClass, page.One("[data-testid=payments-status]")!.ClassName);
        Assert.Single(card.QuerySelectorAll(".btn--primary")); // the next step is the one primary action of the card
        Assert.Equal("btn btn--primary", card.QuerySelector("form button")!.ClassName);
        Assert.StartsWith("Co widzi klient w międzyczasie:", page.TextOf("[data-testid=payments-client-view]"));
    }

    [Fact]
    public async Task A_ready_account_has_no_client_sentence_and_a_success_badge()
    {
        var (web, browser, _) = await PractitionerBrowsers.SignedIn(w => { w.HaveProfile("Approved"); w.PaymentState = "ready"; });
        await using var _ = web;
        var page = await browser.GetAsync("/gabinet-programisty/platnosci");
        Assert.Equal("badge badge--success", page.One("[data-testid=payments-status]")!.ClassName);
        Assert.Null(page.One("[data-testid=payments-client-view]"));
    }

    [Fact]
    public async Task A_ready_account_says_gotowe_and_has_no_button_and_no_warning()
    {
        var (web, browser, _) = await PractitionerBrowsers.SignedIn(w => { w.HaveProfile("Approved"); w.PaymentState = "ready"; });
        await using var _ = web;
        var page = await browser.GetAsync("/gabinet-programisty/platnosci");
        Assert.Equal("Gotowe", page.TextOf("[data-testid=payments-status]"));
        Assert.Null(page.One("form[method=post]"));
        Assert.Null(page.One("[data-testid=payments-must-be-ready]"));
        Assert.Contains("Konto płatności jest gotowe", page.TextOf("[data-testid=payments-body]"));
    }

    [Fact]
    public async Task The_demo_provider_has_nothing_to_set_up_and_says_so()
    {
        var (web, browser, _) = await PractitionerBrowsers.SignedIn(w => { w.HaveProfile("Approved"); w.PaymentState = null; });
        await using var _ = web;
        var page = await browser.GetAsync("/gabinet-programisty/platnosci");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Contains("Tryb demonstracyjny", page.TextOf("[data-testid=payments-body]"));
        Assert.Null(page.One("form[method=post]"));
        Assert.Null(page.One("[data-testid=payments-status]"));
        Assert.Null(page.One("[data-testid=payments-must-be-ready]"));
    }

    [Fact]
    public async Task Starting_shows_the_single_use_link_instead_of_redirecting_and_never_stores_it()
    {
        var (web, browser, world) = await PractitionerBrowsers.SignedIn(w => { w.HaveProfile("Approved"); w.PaymentState = "none"; });
        await using var _ = web;
        var response = await browser.Submit("/gabinet-programisty/platnosci", "payments-start");
        Assert.Equal(HttpStatusCode.OK, response.Status); // no redirect: the CSP's form-action 'self' would block it in a browser
        Assert.Null(response.Location);
        var link = response.One("[data-testid=payments-link] a")!;
        Assert.Equal("https://connect.stripe.com/setup/e/acct_test/abc", link.GetAttribute("href"));
        Assert.Equal("Przejdź do konfiguracji w Stripe", link.TextContent);
        Assert.Equal("noopener noreferrer", link.GetAttribute("rel"));
        Assert.Equal(1, world.OnboardingCalls);
        Assert.StartsWith("Bearer ", web.Booking.Requests.Single(r => r.Method == HttpMethod.Post).Authorization);
        var again = await browser.GetAsync("/gabinet-programisty/platnosci"); // a reload does not show (or request) the link again
        Assert.Null(again.One("[data-testid=payments-link]"));
        Assert.Equal(1, world.OnboardingCalls);
        Assert.Equal("Wymaga uzupełnienia", again.TextOf("[data-testid=payments-status]")); // the provider now knows the account
    }

    [Theory]
    [InlineData("http://connect.stripe.com/x")] [InlineData("javascript:alert(1)")] [InlineData("//evil.example/")] [InlineData("")]
    public async Task A_link_that_is_not_https_is_never_shown(string url)
    {
        var (web, browser, world) = await PractitionerBrowsers.SignedIn(w => { w.HaveProfile("Approved"); w.PaymentState = "none"; });
        await using var _ = web;
        world.OnboardingUrl = url;
        var response = await browser.Submit("/gabinet-programisty/platnosci", "payments-start");
        Assert.Null(response.One("[data-testid=payments-link]"));
        Assert.DoesNotContain("evil.example", response.Html);
        Assert.DoesNotContain("javascript:", response.Html);
        Assert.Equal("Nie udało się przygotować konfiguracji płatności. Spróbuj ponownie za chwilę.", response.TextOf("[data-testid=payments-error]"));
    }

    [Fact]
    public async Task A_provider_that_is_down_when_starting_says_so_and_shows_no_server_text()
    {
        var (web, browser, world) = await PractitionerBrowsers.SignedIn(w => { w.HaveProfile("Approved"); w.PaymentState = "none"; });
        await using var _ = web;
        var page = await browser.GetAsync("/gabinet-programisty/platnosci");
        world.BookingDown = true;
        var form = page.One("form[method=post]")!;
        var hidden = form.QuerySelectorAll("input[type=hidden]").Select(i => KeyValuePair.Create(i.GetAttribute("name")!, i.GetAttribute("value") ?? ""));
        var response = await browser.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/gabinet-programisty/platnosci") { Content = new FormUrlEncodedContent(hidden) });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.Status); // the state could not be read either
        Assert.Equal("alert", response.One("[data-testid=payments-error]")!.GetAttribute("role"));
        Assert.DoesNotContain("SERVER-", response.Html);
    }

    [Fact]
    public async Task The_return_and_refresh_flags_show_their_message_and_anything_else_shows_nothing()
    {
        var (web, browser, _) = await PractitionerBrowsers.SignedIn(w => { w.HaveProfile("Approved"); w.PaymentState = "pending"; });
        await using var _ = web;
        var back = await browser.GetAsync("/gabinet-programisty/platnosci?return=1");
        Assert.Contains("Wróciłeś z konfiguracji", back.TextOf("[data-testid=payments-returned]"));
        Assert.Equal("Wymaga uzupełnienia", back.TextOf("[data-testid=payments-status]")); // the state is read again, fresh
        var refresh = await browser.GetAsync("/gabinet-programisty/platnosci?refresh=1");
        Assert.Contains("Link do konfiguracji wygasł", refresh.TextOf("[data-testid=payments-refresh]"));
        var junk = await browser.GetAsync("/gabinet-programisty/platnosci?return=%3Cscript%3E&refresh=yes");
        Assert.Null(junk.One("[data-testid=payments-returned]"));
        Assert.Null(junk.One("[data-testid=payments-refresh]"));
        Assert.DoesNotContain("<script>alert", junk.Html);
    }

    [Theory]
    [InlineData("/practitioner/payments?return=1", "/gabinet-programisty/platnosci?return=1")]
    [InlineData("/practitioner/payments?refresh=1", "/gabinet-programisty/platnosci?refresh=1")]
    [InlineData("/practitioner/payments", "/gabinet-programisty/platnosci")]
    [InlineData("/practitioner/payments?return=1&x=https://evil.example", "/gabinet-programisty/platnosci?return=1")]
    [InlineData("/practitioner/payments?return=%2F%2Fevil.example", "/gabinet-programisty/platnosci")]
    public async Task The_address_Booking_gives_Stripe_lands_on_the_page_of_this_host_keeping_only_its_two_flags(string landing, string target)
    {
        var (web, browser, _) = await PractitionerBrowsers.SignedIn(w => w.HaveProfile("Approved"));
        await using var _ = web;
        var page = await browser.GetAsync(landing);
        Assert.Contains((int)page.Status, new[] { 302, 303 });
        Assert.Equal(target, page.Location);
    }

    [Fact]
    public async Task A_visitor_who_returns_from_stripe_with_an_ended_session_signs_in_and_comes_back_to_the_landing_address()
    {
        await using var web = new WebFactory();
        var page = await web.NewBrowser().GetAsync("/practitioner/payments?return=1");
        Assert.Equal(HttpStatusCode.Found, page.Status);
        Assert.Equal("/logowanie?redirect=%2Fpractitioner%2Fpayments%3Freturn%3D1", page.Location);
    }

    [Fact]
    public async Task Booking_down_is_a_503_and_an_account_with_no_profile_is_sent_to_the_dashboard()
    {
        var (web, browser, _) = await PractitionerBrowsers.SignedIn(w => { w.HaveProfile("Approved"); w.BookingDown = true; });
        await using var _ = web;
        var down = await browser.GetAsync("/gabinet-programisty/platnosci");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, down.Status);
        Assert.Equal("alert", down.One("[data-testid=unavailable]")!.GetAttribute("role"));
        Assert.True(down.One("form[method=post]")!.HasAttribute("hidden")); // no action to offer while the state is unknown
        var (web2, browser2, _) = await PractitionerBrowsers.SignedIn();
        await using var _2 = web2;
        Assert.Equal("/gabinet-programisty", (await browser2.GetAsync("/gabinet-programisty/platnosci")).Location);
    }
}
