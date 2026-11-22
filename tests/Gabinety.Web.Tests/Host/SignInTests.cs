using System.Net;

namespace Gabinety.Web.Tests.Pipeline;

/// <summary>Cookie authentication on the host, tokens server side only (BLAZOR-WEB-APP section 7), against an identity fake that issues real RS256 tokens.</summary>
public class SignInTests
{
    static WebFactory Web()
    {
        var web = new WebFactory();
        web.Booking.Respond = _ => FakeBackend.Json(new Gabinety.Web.Api.PageDto<Gabinety.Web.Api.BookingViewDto>([Sample.Booking("Konsultacja demo"), Sample.Booking("Druga konsultacja", "PendingPayment")], null));
        return web;
    }

    static Dictionary<string, string> Form(string email = "demo-client@demo.example", string password = FakeIdentity.Password) => new() { ["Model.Email"] = email, ["Model.Password"] = password };

    [Fact]
    public async Task The_login_page_is_a_real_form_with_labels_an_antiforgery_token_and_no_script()
    {
        await using var web = Web();
        var page = await web.NewBrowser().GetAsync("/logowanie?redirect=%2Fmoje%2Frezerwacje");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal("Logowanie", page.TextOf("h1"));
        var form = page.One("form[method=post]")!;
        Assert.Equal("Adres e-mail", form.QuerySelector("label[for=login-email]")!.TextContent);
        Assert.Equal("Hasło", form.QuerySelector("label[for=login-password]")!.TextContent);
        Assert.Equal("Zaloguj się", form.QuerySelector("button[type=submit]")!.TextContent);
        Assert.Equal("password", form.QuerySelector("#login-password")!.GetAttribute("type"));
        Assert.NotNull(form.QuerySelector("input[name=__RequestVerificationToken]"));
        Assert.Equal("login", form.QuerySelector("input[name=_handler]")!.GetAttribute("value"));
        Assert.Equal("noindex,follow", page.One("meta[name=robots]")!.GetAttribute("content"));
    }

    [Fact]
    public async Task A_sign_in_sets_one_cookie_with_the_attributes_of_the_nextjs_app_and_holds_no_token()
    {
        await using var web = Web();
        var browser = web.NewBrowser();
        var response = await browser.PostFormAsync("/logowanie", Form());
        Assert.Equal(HttpStatusCode.Found, response.Status);
        Assert.Equal("/", response.Location);

        var cookie = response.SetCookies.Single(c => c.StartsWith("gt_session="));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", cookie, StringComparison.OrdinalIgnoreCase); // host-only
        Assert.DoesNotContain("secure", cookie, StringComparison.OrdinalIgnoreCase);  // the site is http here
        Assert.Matches(@"expires=", cookie.ToLowerInvariant());
        var value = cookie.Split(';')[0]["gt_session=".Length..];
        Assert.True(value.Length > 40);
        Assert.DoesNotContain("eyJ", value); // no JWT in the cookie
        Assert.DoesNotContain("refresh-", value);
        Assert.Equal(1, browser.Cookies.Keys.Count(k => k == "gt_session"));
        Assert.DoesNotContain(response.SetCookies, c => c.Contains("gt_at") || c.Contains("gt_rt"));
    }

    [Fact]
    public async Task The_cookie_is_Secure_when_the_site_is_served_over_https()
    {
        await using var web = Web().With("Site:BaseUrl", "https://gabinety.example");
        var response = await web.NewBrowser().PostFormAsync("/logowanie", Form(), headers: ("Origin", "https://gabinety.example"));
        Assert.Contains("secure", response.SetCookies.Single(c => c.StartsWith("gt_session=")), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_signed_in_user_reaches_the_authenticated_page_and_the_backend_gets_the_bearer_the_browser_never_had()
    {
        await using var web = Web();
        var browser = web.NewBrowser();
        await SignIn.As(web, browser, returnTo: "/moje/rezerwacje");

        var page = await browser.GetAsync("/moje/rezerwacje");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal("Moje rezerwacje", page.TextOf("h1"));
        Assert.Equal(["Konsultacja demo", "Druga konsultacja"], page.All("[data-testid=booking-row] .booking-row__title").Select(h => h.TextContent));
        Assert.Contains("wtorek, 1 grudnia, 13:00–14:00", page.TextOf("[data-testid=booking-row]"));
        Assert.Contains("250,00 zł", page.TextOf("[data-testid=booking-row]"));
        Assert.Equal("Potwierdzona", page.One("[data-testid=booking-row] .badge")!.TextContent);

        var seen = web.Booking.Requests.Last();
        Assert.Equal("/api/v1/me/bookings?role=client", seen.PathAndQuery);
        Assert.StartsWith("Bearer eyJ", seen.Authorization);
        Assert.DoesNotContain("eyJ", page.Html); // and no token anywhere in the page, prerendered state included
        Assert.DoesNotContain(web.Identity.Backend.Requests[0].Body ?? "", page.Html);
        Assert.Equal(["/programisci", "/jak-to-dziala", "/cennik"], page.All("header .site-nav a").Select(a => a.GetAttribute("href")));
        Assert.Equal(["/moje/rezerwacje", "/konto", "/gabinet-programisty", "/wyloguj"], page.All("header .account-menu a").Select(a => a.GetAttribute("href")));
    }

    [Fact]
    public async Task The_page_reads_the_role_and_status_from_the_url_so_a_link_reopens_the_same_view()
    {
        await using var web = Web();
        var browser = web.NewBrowser();
        await SignIn.As(web, browser);
        await browser.GetAsync("/moje/rezerwacje?role=practitioner&status=Confirmed");
        Assert.Equal("/api/v1/me/bookings?role=practitioner&status=Confirmed", web.Booking.Requests.Last().PathAndQuery);
        await browser.GetAsync("/moje/rezerwacje?role=admin&status=Hacked%26x");
        Assert.Equal("/api/v1/me/bookings?role=client", web.Booking.Requests.Last().PathAndQuery); // anything else is the default view
    }

    [Fact]
    public async Task Without_a_session_an_authenticated_page_redirects_to_sign_in_with_the_target_preserved()
    {
        await using var web = Web();
        var page = await web.NewBrowser().GetAsync("/moje/rezerwacje?role=practitioner&status=Confirmed");
        Assert.Equal(HttpStatusCode.Found, page.Status);
        Assert.Equal("/logowanie?redirect=%2Fmoje%2Frezerwacje%3Frole%3Dpractitioner%26status%3DConfirmed", page.Location);
        Assert.Empty(web.Booking.Requests); // nothing was asked of the backend for an anonymous visitor
    }

    [Theory]
    [InlineData("/moje/rezerwacje", "/moje/rezerwacje")]
    [InlineData("/moje/rezerwacje?status=Confirmed", "/moje/rezerwacje?status=Confirmed")]
    [InlineData("//evil.example/phish", "/")]
    [InlineData("https://evil.example", "/")]
    [InlineData("/\\evil.example", "/")]
    [InlineData("/api/anything", "/")]
    [InlineData("/logowanie?redirect=/x", "/")]
    public async Task After_signing_in_the_user_goes_to_the_filtered_return_target(string redirect, string expected)
    {
        await using var web = Web();
        var response = await web.NewBrowser().PostFormAsync($"/logowanie?redirect={Uri.EscapeDataString(redirect)}", Form());
        Assert.Equal(HttpStatusCode.Found, response.Status);
        Assert.Equal(expected, response.Location);
    }

    [Fact]
    public async Task A_wrong_password_says_so_in_an_alert_sets_no_cookie_and_does_not_echo_the_password()
    {
        await using var web = Web();
        var browser = web.NewBrowser();
        var response = await browser.PostFormAsync("/logowanie", Form(password: "Wrong-Passw0rd-xyz"));
        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal("Nieprawidłowy adres e-mail lub hasło.", response.TextOf("[role=alert]"));
        Assert.DoesNotContain(response.SetCookies, c => c.StartsWith("gt_session="));
        Assert.DoesNotContain("Wrong-Passw0rd-xyz", response.Html);
        Assert.Equal("demo-client@demo.example", response.One("#login-email")!.GetAttribute("value")); // the e-mail is kept for the retry
    }

    [Fact]
    public async Task An_empty_form_is_a_validation_message_not_a_call_to_the_identity_service()
    {
        await using var web = Web();
        var response = await web.NewBrowser().PostFormAsync("/logowanie", Form("", ""));
        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Contains("Podaj adres e-mail.", response.TextOf("[data-testid=form-summary]")); // every problem at once, with a link to its field
        Assert.Contains("Podaj hasło.", response.TextOf("[data-testid=form-summary]"));
        Assert.Equal("Popraw 2 pola", response.TextOf("[data-testid=form-summary] h2"));
        Assert.Equal("true", response.One("#login-email")!.GetAttribute("aria-invalid"));
        Assert.DoesNotContain(web.Identity.Backend.Requests, r => r.PathAndQuery.Contains("/auth/login"));
    }

    [Fact]
    public async Task A_token_that_does_not_verify_is_never_trusted_and_signs_nobody_in()
    {
        await using var web = Web();
        using var attacker = FakeIdentity.NewKey();
        web.Identity.Backend.Respond = r => r.RequestUri!.AbsolutePath == "/.well-known/jwks.json"
            ? FakeBackend.Raw(web.Identity.Jwks())
            : FakeBackend.Json(new { accessToken = web.Identity.Issue(roles: ["Admin"], signWith: attacker), refreshToken = "r", expiresIn = 3600 }); // a login that hands out a forged admin token
        var response = await web.NewBrowser().PostFormAsync("/logowanie", Form());
        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Contains("Logowanie jest chwilowo niedostępne", response.TextOf("[role=alert]"));
        Assert.DoesNotContain(response.SetCookies, c => c.StartsWith("gt_session="));
    }

    [Fact]
    public async Task An_identity_service_that_is_down_is_an_alert_not_a_crash()
    {
        await using var web = Web();
        web.Identity.Down = true;
        var response = await web.NewBrowser().PostFormAsync("/logowanie", Form());
        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Contains("chwilowo niedostępne", response.TextOf("[role=alert]"));
    }

    [Fact]
    public async Task A_signed_in_visitor_on_the_login_page_is_sent_on()
    {
        await using var web = Web();
        var browser = web.NewBrowser();
        await SignIn.As(web, browser);
        var page = await browser.GetAsync("/logowanie?redirect=%2Fmoje%2Frezerwacje");
        Assert.Equal(HttpStatusCode.Found, page.Status);
        Assert.Equal("/moje/rezerwacje", page.Location);
    }

    [Fact]
    public async Task Signing_out_ends_the_session_everywhere_and_the_old_cookie_is_worthless()
    {
        await using var web = Web();
        var browser = web.NewBrowser();
        var sid = await SignIn.As(web, browser);
        var confirm = await browser.GetAsync("/wyloguj"); // a page with a form: a GET never signs anybody out
        Assert.Equal(HttpStatusCode.OK, confirm.Status);
        Assert.Equal("Wylogowanie", confirm.TextOf("h1"));

        var response = await browser.PostFormAsync("/wyloguj", new Dictionary<string, string>());
        Assert.Equal(HttpStatusCode.Found, response.Status);
        Assert.Equal("/?flash=signed-out", response.Location);
        Assert.Contains("/api/v1/auth/logout", web.Identity.Backend.Requests.Select(r => r.PathAndQuery)); // the identity service is told
        Assert.Contains(response.SetCookies, c => c.StartsWith("gt_session=;") && c.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase)); // deleted with the same attributes

        var replay = web.NewBrowser();
        replay.Client.DefaultRequestHeaders.Add("Cookie", $"gt_session={sid}");
        var page = await replay.GetAsync("/moje/rezerwacje");
        Assert.Equal(HttpStatusCode.Found, page.Status); // the replayed cookie finds no session
        Assert.StartsWith("/logowanie?redirect=", page.Location);
        var home = await browser.GetAsync("/?flash=signed-out");
        Assert.Equal("Wylogowano.", home.TextOf("[data-testid=flash]"));
    }

    [Fact]
    public async Task A_made_up_or_tampered_session_cookie_is_anonymous()
    {
        await using var web = Web();
        foreach (var cookie in new[] { "gt_session=AAAA", "gt_session=eyJhbGciOiJub25lIn0.eyJzdWIiOiIxIn0.", "gt_session=" + new string('x', 200) })
        {
            var b = web.NewBrowser();
            b.Client.DefaultRequestHeaders.Add("Cookie", cookie);
            Assert.Equal(HttpStatusCode.Found, (await b.GetAsync("/moje/rezerwacje")).Status);
        }
    }

    [Fact]
    public async Task The_authenticated_page_says_so_in_an_alert_when_the_booking_service_is_down_and_the_shell_stays()
    {
        await using var web = Web();
        var browser = web.NewBrowser();
        await SignIn.As(web, browser);
        web.Booking.Respond = FakeBackend.Throw(new HttpRequestException("down"));
        var page = await browser.GetAsync("/moje/rezerwacje");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal("Brak połączenia z usługą rezerwacji. Spróbuj ponownie za chwilę.", page.TextOf("[data-testid=unavailable] p"));
        Assert.Equal("alert", page.One("[data-testid=unavailable]")!.GetAttribute("role"));
        Assert.Equal("Moje rezerwacje", page.TextOf("h1"));
        Assert.NotNull(page.One("a[href='/wyloguj']"));
    }
}
