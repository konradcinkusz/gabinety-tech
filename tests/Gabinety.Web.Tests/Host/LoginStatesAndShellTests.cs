using System.Net;

namespace Gabinety.Web.Tests.Pipeline;

/// <summary>The sign-in states authservice has and DevIdentity never shows (429, lockout, two-factor), and the shell by session (DESIGN-CONTRACT 5.1, audit G-1 G-2 G-4).</summary>
public class LoginStatesTests
{
    static WebFactory Web() { var w = new WebFactory(); w.Booking.Respond = _ => FakeBackend.Json(new Gabinety.Web.Api.PageDto<Gabinety.Web.Api.BookingViewDto>([], null)); return w; }
    static Dictionary<string, string> Form(string password = FakeIdentity.Password) => new() { ["Model.Email"] = "demo-client@demo.example", ["Model.Password"] = password };

    [Fact]
    public async Task A_throttled_login_says_wait_and_a_wrong_password_says_wrong_they_are_different_texts()
    {
        await using var web = Web();
        var wrong = await web.NewBrowser().PostFormAsync("/logowanie", Form("Wrong-Passw0rd!"));
        Assert.Equal("Nieprawidłowy adres e-mail lub hasło.", wrong.TextOf("[data-testid=login-error]"));
        web.Identity.LoginStatus = 429;
        var throttled = await web.NewBrowser().PostFormAsync("/logowanie", Form());
        Assert.Equal("Zbyt wiele prób logowania. Spróbuj ponownie za minutę.", throttled.TextOf("[data-testid=login-error]"));
        Assert.DoesNotContain(throttled.SetCookies, c => c.StartsWith("gt_session="));
        Assert.Equal("alert", throttled.One("[data-testid=login-error]")!.ParentElement!.GetAttribute("role")); // the message sits in the live region that exists before any message
        Assert.Single(web.Identity.Backend.Requests, r => r.PathAndQuery.Contains("/auth/login") && r.Body!.Contains(FakeIdentity.Password)); // a login is never retried
    }

    [Fact]
    public async Task A_locked_account_gets_the_lockout_text_without_a_time_or_a_reason()
    {
        await using var web = Web();
        web.Identity.LockedOut = true;
        var r = await web.NewBrowser().PostFormAsync("/logowanie", Form());
        Assert.Equal("Konto jest chwilowo zablokowane po wielu nieudanych próbach logowania. Spróbuj ponownie za kilka minut.", r.TextOf("[data-testid=login-error]"));
        Assert.DoesNotContain("2026-12", r.Html); // the lockout end of the service is not shown
        Assert.DoesNotContain("lockout", r.Html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(r.SetCookies, c => c.StartsWith("gt_session="));
    }

    [Theory] [InlineData(500)] [InlineData(503)]
    public async Task A_failing_identity_service_is_unavailable_not_wrong_password(int status)
    {
        await using var web = Web();
        web.Identity.LoginStatus = status;
        Assert.Contains("chwilowo niedostępne", (await web.NewBrowser().PostFormAsync("/logowanie", Form())).TextOf("[data-testid=login-error]"));
    }

    [Fact]
    public async Task The_page_offers_github_and_registration_and_both_keep_the_return_target()
    {
        await using var web = Web();
        var page = await web.NewBrowser().GetAsync("/logowanie?redirect=%2Fmoje%2Frezerwacje");
        Assert.Equal("/oauth/github/start?redirect=%2Fmoje%2Frezerwacje", page.All("main a.btn").Single(a => a.TextContent == "Zaloguj się przez GitHub").GetAttribute("href"));
        Assert.Equal("/rejestracja?redirect=%2Fmoje%2Frezerwacje", page.All("main a").Single(a => a.TextContent == "Załóż konto").GetAttribute("href"));
        var plain = await web.NewBrowser().GetAsync("/logowanie");
        Assert.Equal("/oauth/github/start", plain.All("main a.btn").Single(a => a.TextContent.Contains("GitHub")).GetAttribute("href"));
    }

    [Fact]
    public async Task The_github_failure_and_the_expired_challenge_arrive_as_fixed_texts_and_any_other_error_key_is_ignored()
    {
        await using var web = Web();
        Assert.Equal("Logowanie przez GitHub nie powiodło się. Spróbuj ponownie lub użyj hasła.", (await web.NewBrowser().GetAsync("/logowanie?error=github_failed")).TextOf("[data-testid=login-error]"));
        Assert.Equal("Sesja wygasła. Zaloguj się ponownie.", (await web.NewBrowser().GetAsync("/logowanie?error=session_expired")).TextOf("[data-testid=login-error]"));
        var injected = await web.NewBrowser().GetAsync("/logowanie?error=%3Cb%3Ehi%3C%2Fb%3E");
        Assert.Null(injected.One("[data-testid=login-error]"));
        Assert.DoesNotContain("<b>hi", injected.Html);
    }

    // ---- two factor ----

    static async Task<(Browser Browser, Page Challenge)> FirstStep(WebFactory web, string redirect = "/moje/rezerwacje")
    {
        web.Identity.RequireTwoFactor = true;
        var browser = web.NewBrowser();
        var first = await browser.PostFormAsync($"/logowanie?redirect={Uri.EscapeDataString(redirect)}", Form());
        return (browser, first);
    }

    [Fact]
    public async Task A_password_step_that_needs_a_code_leaves_a_sealed_challenge_and_sends_the_visitor_to_the_code_page()
    {
        await using var web = Web();
        var (browser, first) = await FirstStep(web);
        Assert.Equal(HttpStatusCode.Found, first.Status);
        Assert.Equal("/logowanie/kod?redirect=%2Fmoje%2Frezerwacje", first.Location);
        Assert.DoesNotContain(first.SetCookies, c => c.StartsWith("gt_session="));
        var cookie = first.SetCookies.Single(c => c.StartsWith("gt_2fa="));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/logowanie", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max-age=300", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("challenge-1", cookie); // sealed

        var page = await browser.GetAsync(first.Location!);
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal("Kod weryfikacyjny", page.TextOf("h1"));
        Assert.Equal("Kod z aplikacji", page.TextOf("label[for=two-factor-code]"));
        Assert.Equal("one-time-code", page.One("#two-factor-code")!.GetAttribute("autocomplete"));
        Assert.Equal("numeric", page.One("#two-factor-code")!.GetAttribute("inputmode"));
        Assert.DoesNotContain("challenge-1", page.Html); // the challenge never reaches the page
        Assert.Equal("/logowanie/kod?redirect=%2Fmoje%2Frezerwacje&recovery=1", page.All("main a").Single(a => a.TextContent == "Użyj kodu odzyskiwania").GetAttribute("href"));
    }

    [Fact]
    public async Task The_right_code_completes_the_sign_in_clears_the_challenge_and_goes_to_the_original_target()
    {
        await using var web = Web();
        var (browser, _) = await FirstStep(web);
        var done = await browser.PostFormAsync("/logowanie/kod?redirect=%2Fmoje%2Frezerwacje", new Dictionary<string, string> { ["Model.Code"] = "123 456" });
        Assert.Equal(HttpStatusCode.Found, done.Status);
        Assert.Equal("/moje/rezerwacje", done.Location);
        Assert.Contains(done.SetCookies, c => c.StartsWith("gt_session="));
        Assert.Contains(done.SetCookies, c => c.StartsWith("gt_2fa=;") && c.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("\"code\":\"123456\"", web.Identity.Backend.Requests.Single(r => r.PathAndQuery == "/api/v1/auth/2fa/login").Body); // spaces dropped
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/moje/rezerwacje")).Status);
    }

    [Fact]
    public async Task A_wrong_code_keeps_the_challenge_for_a_retry_and_says_so_without_echoing_the_code()
    {
        await using var web = Web();
        var (browser, _) = await FirstStep(web);
        var wrong = await browser.PostFormAsync("/logowanie/kod", new Dictionary<string, string> { ["Model.Code"] = "000000" });
        Assert.Equal(HttpStatusCode.OK, wrong.Status);
        Assert.Equal("Nieprawidłowy kod. Sprawdź go i spróbuj ponownie.", wrong.TextOf("[data-testid=two-factor-error]"));
        Assert.DoesNotContain("000000", wrong.Html);
        Assert.DoesNotContain(wrong.SetCookies, c => c.StartsWith("gt_session=") || c.StartsWith("gt_2fa=;"));
        var retry = await browser.PostFormAsync("/logowanie/kod", new Dictionary<string, string> { ["Model.Code"] = FakeIdentity.TwoFactorCode });
        Assert.Equal(HttpStatusCode.Found, retry.Status);
        Assert.Contains(retry.SetCookies, c => c.StartsWith("gt_session="));
    }

    [Fact]
    public async Task An_empty_code_is_a_validation_message_and_asks_nothing_of_the_identity_service()
    {
        await using var web = Web();
        var (browser, _) = await FirstStep(web);
        var r = await browser.PostFormAsync("/logowanie/kod", new Dictionary<string, string> { ["Model.Code"] = " " });
        Assert.Equal("Wpisz kod.", r.TextOf("#two-factor-code-error").Replace("Błąd:", "").Trim());
        Assert.Equal("Popraw 1 pole", r.TextOf("[data-testid=form-summary] h2"));
        Assert.DoesNotContain(web.Identity.Backend.Requests, x => x.PathAndQuery.Contains("2fa"));
    }

    [Fact]
    public async Task A_recovery_code_works_once()
    {
        await using var web = Web();
        var (browser, _) = await FirstStep(web);
        var page = await browser.GetAsync("/logowanie/kod?recovery=1");
        Assert.Equal("Kod odzyskiwania", page.TextOf("label[for=two-factor-code]"));
        var done = await browser.PostFormAsync("/logowanie/kod?recovery=1", new Dictionary<string, string> { ["Model.Code"] = " recovery-0001 ".ToUpperInvariant() });
        Assert.Equal(HttpStatusCode.Found, done.Status);
        Assert.Contains("\"recoveryCode\":\"RECOVERY-0001\"", web.Identity.Backend.Requests.Single(r => r.PathAndQuery == "/api/v1/auth/2fa/login").Body); // spaces dropped, the hyphen is part of the code
    }

    [Fact]
    public async Task The_code_page_without_a_challenge_or_with_a_tampered_one_goes_back_to_sign_in()
    {
        await using var web = Web();
        var none = await web.NewBrowser().GetAsync("/logowanie/kod");
        Assert.Equal("/logowanie?error=session_expired", none.Location);
        var forged = await web.NewBrowser().WithCookie("gt_2fa", "CfDJ8-forged-value").GetAsync("/logowanie/kod?redirect=%2Fmoje%2Frezerwacje");
        Assert.Equal("/logowanie?redirect=%2Fmoje%2Frezerwacje&error=session_expired", forged.Location);
        var post = await web.Client().PostAsync("/logowanie/kod", new FormUrlEncodedContent(new Dictionary<string, string> { ["_handler"] = "two-factor", ["Model.Code"] = FakeIdentity.TwoFactorCode }));
        Assert.Equal(HttpStatusCode.BadRequest, post.StatusCode); // no page, no token, no challenge: nothing is accepted
        Assert.DoesNotContain(post.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : [], c => c.StartsWith("gt_session="));
        Assert.DoesNotContain(web.Identity.Backend.Requests, x => x.PathAndQuery.Contains("2fa"));
    }

    [Fact]
    public async Task A_challenge_of_a_foreign_target_cannot_redirect_off_the_site()
    {
        await using var web = Web();
        web.Identity.RequireTwoFactor = true;
        var browser = web.NewBrowser();
        await browser.PostFormAsync("/logowanie?redirect=https%3A%2F%2Fevil.example", Form());
        var done = await browser.PostFormAsync("/logowanie/kod", new Dictionary<string, string> { ["Model.Code"] = FakeIdentity.TwoFactorCode });
        Assert.Equal("/", done.Location);
    }

    [Fact]
    public async Task A_throttled_second_step_keeps_the_challenge_and_says_wait()
    {
        await using var web = Web();
        var (browser, _) = await FirstStep(web);
        var original = web.Identity.Backend.Respond;
        web.Identity.Backend.Respond = r => r.RequestUri!.AbsolutePath.EndsWith("2fa/login") ? new HttpResponseMessage((HttpStatusCode)429) : original(r);
        var r = await browser.PostFormAsync("/logowanie/kod", new Dictionary<string, string> { ["Model.Code"] = FakeIdentity.TwoFactorCode });
        Assert.Equal("Zbyt wiele prób. Spróbuj ponownie za minutę.", r.TextOf("[data-testid=two-factor-error]"));
        Assert.DoesNotContain(r.SetCookies, c => c.StartsWith("gt_2fa=;"));
    }
}

public class ShellTests
{
    static WebFactory Web()
    {
        var web = new WebFactory();
        web.Directory.Respond = r => r.RequestUri!.AbsolutePath switch { "/api/v1/taxonomy" => FakeBackend.Json(Sample.Taxonomy), "/api/v1/sitemap" => FakeBackend.Json(Sample.Sitemap()), "/api/v1/me" => FakeBackend.Json(new Gabinety.Web.Api.MeDto("user-1", null, null, false, false)), _ => FakeBackend.Json(Sample.Cards(2)) };
        web.Booking.Respond = _ => FakeBackend.Json(new Gabinety.Web.Api.PageDto<Gabinety.Web.Api.BookingViewDto>([], null));
        return web;
    }

    static string[] Hrefs(Page p, string css) => [.. p.All(css).Select(a => a.GetAttribute("href")!)];

    [Fact]
    public async Task A_visitor_sees_the_two_buttons_the_sign_in_one_keeps_the_page_and_there_is_no_account_menu()
    {
        await using var web = Web();
        var page = await web.NewBrowser().GetAsync("/programisci?q=kod");
        Assert.Equal(["/logowanie?redirect=%2Fprogramisci%3Fq%3Dkod", "/rejestracja?redirect=%2Fprogramisci%3Fq%3Dkod"], Hrefs(page, ".site-account a"));
        Assert.Equal(["Zaloguj się", "Załóż konto"], page.All(".site-account a").Select(a => a.TextContent));
        Assert.Contains("btn--ghost", page.One(".site-account a")!.ClassName);
        Assert.Contains("btn--primary", page.All(".site-account a")[1].ClassName);
        Assert.Null(page.One("details.account-menu"));
        Assert.DoesNotContain("Wyloguj", page.One("header")!.TextContent);
    }

    [Fact]
    public async Task The_home_page_links_without_a_target_and_the_sign_in_pages_do_not_loop()
    {
        await using var web = Web();
        Assert.Equal(["/logowanie", "/rejestracja"], Hrefs(await web.NewBrowser().GetAsync("/"), ".site-account a"));
        Assert.Equal(["/logowanie", "/rejestracja"], Hrefs(await web.NewBrowser().GetAsync("/logowanie?redirect=%2Fmoje%2Frezerwacje"), ".site-account a"));
    }

    [Fact]
    public async Task A_client_sees_the_primary_links_the_account_menu_with_the_way_to_become_a_programmer_and_sign_out_but_no_administration()
    {
        await using var web = Web();
        var browser = web.NewBrowser();
        await SignIn.As(web, browser);
        var page = await browser.GetAsync("/programisci");
        Assert.Equal(["/programisci", "/jak-to-dziala", "/cennik"], Hrefs(page, ".site-nav a"));
        Assert.Equal(["/moje/rezerwacje", "/konto", "/gabinet-programisty", "/wyloguj"], Hrefs(page, ".account-menu a"));
        Assert.Equal(["Moje rezerwacje", "Konto", "Zostań programistą", "Wyloguj się"], page.All(".account-menu a").Select(a => a.TextContent));
        Assert.DoesNotContain("Panel programisty", page.One("header")!.TextContent);
        Assert.Null(page.One(".site-account .btn--primary"));
        Assert.DoesNotContain(Hrefs(page, "header a"), h => h.StartsWith("/admin"));
        Assert.DoesNotContain("Zaloguj się", page.One("header")!.TextContent);
    }

    [Fact]
    public async Task The_account_trigger_shows_the_initials_and_the_e_mail_with_the_full_address_in_its_title()
    {
        await using var web = Web();
        var browser = web.NewBrowser();
        await SignIn.As(web, browser);
        var page = await browser.GetAsync("/programisci");
        var trigger = page.One(".account-menu summary")!;
        Assert.Equal("DC", trigger.QuerySelector(".account-menu__avatar")!.TextContent);
        Assert.Equal("true", trigger.QuerySelector(".account-menu__avatar")!.GetAttribute("aria-hidden"));
        var email = trigger.QuerySelector(".account-menu__email")!;
        Assert.Equal("demo-client@demo.example", email.TextContent);
        Assert.Equal("demo-client@demo.example", email.GetAttribute("title"));
        Assert.Single(page.All("[data-testid=account-menu]"));
    }

    [Theory]
    [InlineData("Admin")] [InlineData("SuperAdmin")]
    public async Task An_administrator_also_sees_administration_whether_the_role_claim_is_a_string_or_part_of_an_array(string role)
    {
        await using var web = Web();
        web.Identity.Roles = [role];
        var single = web.NewBrowser();
        await SignIn.As(web, single);
        Assert.Contains("/admin/weryfikacja", Hrefs(await single.GetAsync("/programisci"), ".account-menu a"));

        web.Identity.Roles = ["User", role]; // two roles: an array on the wire
        var many = web.NewBrowser();
        await SignIn.As(web, many);
        var page = await many.GetAsync("/programisci");
        Assert.Contains("/admin/weryfikacja", Hrefs(page, ".account-menu a"));
        Assert.Equal(["Administracja", "Wyloguj się"], page.All(".account-menu a").TakeLast(2).Select(a => a.TextContent));
    }

    [Fact]
    public async Task A_plain_user_role_does_not_unlock_the_administration_link()
    {
        await using var web = Web();
        web.Identity.Roles = ["User"];
        var b = web.NewBrowser();
        await SignIn.As(web, b);
        Assert.DoesNotContain("/admin/weryfikacja", Hrefs(await b.GetAsync("/programisci"), "header a"));
    }

    [Theory]
    [InlineData("/programisci", "Programiści")]
    [InlineData("/programisci/audyt-kodu", "Programiści")]
    [InlineData("/jak-to-dziala", "Jak to działa")]
    [InlineData("/moje/rezerwacje", "Moje rezerwacje")]
    public async Task The_current_section_carries_aria_current_page_and_only_that_one(string path, string label)
    {
        await using var web = Web();
        var browser = web.NewBrowser();
        await SignIn.As(web, browser);
        var page = await browser.GetAsync(path);
        var current = page.All("header .site-nav a[aria-current=page], header .account-menu a[aria-current=page]"); // the desktop rendering; the phone menu is checked below
        Assert.Equal([label], current.Select(a => a.TextContent));
        Assert.Single(page.All(".site-menu a[aria-current=page]")); // the phone menu says the same
    }

    [Fact]
    public async Task A_practitioner_sees_the_panel_in_the_menu_and_the_footer_and_the_directory_is_asked_once_a_minute_per_session()
    {
        await using var web = Web();
        Practitioner(web, "018f0000-0000-7000-8000-0000000000e1");
        var browser = web.NewBrowser();
        await SignIn.As(web, browser);
        var before = MeCalls(web);
        var page = await browser.GetAsync("/programisci");
        Assert.Equal(1, MeCalls(web) - before); // the header asks, the footer reads the same answer
        Assert.Contains("Panel programisty", Labels(page, ".account-menu a"));
        Assert.DoesNotContain("Zostań programistą", page.One("header")!.TextContent);
        Assert.Contains("Panel programisty", Labels(page, "footer a"));
        Assert.DoesNotContain("Zostań programistą", Labels(page, "footer a"));

        await browser.GetAsync("/konto");
        await browser.GetAsync("/jak-to-dziala");
        Assert.Equal(1, MeCalls(web) - before); // within the minute the session's answer is kept
    }

    [Fact]
    public async Task When_the_directory_does_not_answer_the_account_is_offered_the_way_in_not_the_panel()
    {
        await using var web = Web();
        var inner = web.Directory.Respond;
        web.Directory.Respond = r => r.RequestUri!.AbsolutePath == "/api/v1/me" ? new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError) : inner(r);
        var browser = web.NewBrowser();
        await SignIn.As(web, browser);
        var page = await browser.GetAsync("/programisci");
        Assert.Contains("Zostań programistą", Labels(page, ".account-menu a"));
        Assert.DoesNotContain("Panel programisty", page.One("header")!.TextContent);
        Assert.DoesNotContain("Panel programisty", page.One("footer")!.TextContent);
    }

    /// <summary>Makes the Directory's <c>/api/v1/me</c> answer as a practitioner with that profile (or as a client when <paramref name="profileId"/> is null).</summary>
    static void Practitioner(WebFactory web, string? profileId)
    {
        var inner = web.Directory.Respond;
        var id = profileId is null ? (Guid?)null : Guid.Parse(profileId);
        web.Directory.Respond = r => r.RequestUri!.AbsolutePath == "/api/v1/me"
            ? FakeBackend.Json(new Gabinety.Web.Api.MeDto("user-1", id, id is null ? null : "Approved", false, false))
            : inner(r);
    }

    static int MeCalls(WebFactory web) => web.Directory.Requests.Count(r => r.PathAndQuery == "/api/v1/me");

    static string[] Labels(Page p, string css) => [.. p.All(css).Select(a => a.TextContent)];

    [Fact]
    public async Task The_account_page_marks_the_account_menu_entry_current()
    {
        await using var web = Web();
        web.Identity.AccountStatus = 0;
        var browser = web.NewBrowser();
        await SignIn.As(web, browser);
        var page = await browser.GetAsync("/konto");
        Assert.Equal(["Konto"], page.All(".account-menu a[aria-current=page]").Select(a => a.TextContent));
    }

    [Fact]
    public async Task The_phone_menu_is_one_native_details_with_the_same_links_and_no_script()
    {
        await using var web = Web();
        var browser = web.NewBrowser();
        var visitor = await browser.GetAsync("/programisci");
        var menu = visitor.One("header details.site-menu")!;
        Assert.Equal("Menu", menu.QuerySelector("summary")!.TextContent);
        Assert.Null(menu.GetAttribute("open")); // closed until opened (and a new page is a new, closed menu)
        Assert.Equal(["/programisci", "/jak-to-dziala", "/cennik", "/logowanie?redirect=%2Fprogramisci", "/rejestracja?redirect=%2Fprogramisci"], menu.QuerySelectorAll("a").Select(a => a.GetAttribute("href")));
        Assert.Empty(visitor.All("header script"));

        await SignIn.As(web, browser);
        var member = await browser.GetAsync("/programisci");
        Assert.Equal(["/programisci", "/jak-to-dziala", "/cennik", "/moje/rezerwacje", "/konto", "/gabinet-programisty", "/wyloguj"], member.One("header details.site-menu")!.QuerySelectorAll("a").Select(a => a.GetAttribute("href")));
    }

    [Fact]
    public async Task The_page_has_a_skip_link_to_main_one_main_landmark_named_navigations_and_a_footer_in_three_columns()
    {
        await using var web = Web();
        var page = await web.NewBrowser().GetAsync("/programisci");
        Assert.Equal("#main", page.One("a.skip-link")!.GetAttribute("href"));
        Assert.Equal("Przejdź do treści", page.One("a.skip-link")!.TextContent);
        Assert.Equal("main", page.One("#main")!.TagName.ToLowerInvariant());
        Assert.Single(page.All("main"));
        Assert.Equal(["Menu główne", "Menu główne"], page.All("header nav").Select(n => n.GetAttribute("aria-label")));
        var footer = page.One("footer")!;
        Assert.Equal(["Produkt", "Dla programistów", "Prawne"], footer.QuerySelectorAll(".site-footer__title").Select(t => t.TextContent));
        Assert.Equal(3, footer.QuerySelectorAll("nav").Length);
        Assert.Equal(["/programisci", "/jak-to-dziala", "/cennik", "/zasady-rankingu", "/rejestracja", "/regulamin", "/polityka-prywatnosci"], footer.QuerySelectorAll("a").Select(a => a.GetAttribute("href")));
        Assert.Equal(["Programiści", "Jak to działa", "Cennik", "Zasady rankingu", "Zostań programistą", "Regulamin", "Polityka prywatności"], footer.QuerySelectorAll("a").Select(a => a.TextContent));
        Assert.Matches(@"© \d{4} Gabinety Tech", footer.TextContent);
    }

    [Fact]
    public async Task The_brand_is_the_link_text_and_the_logo_mark_is_decorative()
    {
        await using var web = Web();
        var brand = (await web.NewBrowser().GetAsync("/programisci")).One("header a.brand")!;
        Assert.Equal("Gabinety Tech", brand.TextContent.Trim());
        Assert.Equal("true", brand.QuerySelector("svg.brand__mark")!.GetAttribute("aria-hidden"));
        Assert.Equal("currentColor", brand.QuerySelector("svg.brand__mark rect")!.GetAttribute("fill"));
    }

    [Fact]
    public async Task The_flash_region_is_in_the_page_from_the_first_render_and_shows_known_messages_only()
    {
        await using var web = Web();
        var page = await web.NewBrowser().GetAsync("/programisci");
        var region = page.One("[data-testid=flash]")!;
        Assert.Equal("status", region.GetAttribute("role"));
        Assert.Equal("polite", region.GetAttribute("aria-live"));
        Assert.Equal("", region.TextContent);
        Assert.Equal("Wylogowano.", (await web.NewBrowser().GetAsync("/?flash=signed-out")).TextOf("[data-testid=flash]"));
    }

    [Fact]
    public async Task Sign_out_from_the_menu_leads_to_the_confirmation_page_and_ends_in_the_signed_out_header()
    {
        await using var web = Web();
        var browser = web.NewBrowser();
        await SignIn.As(web, browser);
        var menu = await browser.GetAsync("/programisci");
        Assert.Equal("/wyloguj", menu.All(".account-menu a").Last().GetAttribute("href"));
        var done = await browser.PostFormAsync("/wyloguj", new Dictionary<string, string>());
        var home = await browser.GetAsync(done.Location!);
        Assert.Equal("Wylogowano.", home.TextOf("[data-testid=flash]"));
        Assert.Equal(["Zaloguj się", "Załóż konto"], home.All(".site-account a").Select(a => a.TextContent));
    }
}

public class FooterLinkTests
{
    [Theory]
    [InlineData("/regulamin")] [InlineData("/polityka-prywatnosci")]
    public async Task The_legal_links_of_the_footer_lead_to_real_pages_and_the_registration_consent_links_too(string path)
    {
        await using var web = new WebFactory();
        var browser = web.NewBrowser();
        var footerHrefs = (await browser.GetAsync("/logowanie")).All("footer a").Select(a => a.GetAttribute("href")).ToList();
        Assert.Contains(path, footerHrefs);
        var page = await browser.GetAsync(path);
        Assert.Equal(System.Net.HttpStatusCode.OK, page.Status);
        Assert.Single(page.All("h1"));
    }
}
