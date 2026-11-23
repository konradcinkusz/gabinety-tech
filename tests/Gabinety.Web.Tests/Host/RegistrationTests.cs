using System.Net;

namespace Gabinety.Web.Tests.Pipeline;

/// <summary>/rejestracja: static form post, every problem at once (F-06), no internal versions printed (F-07), the 202 and 2FA states as real pages (ADR-0010 task 2).</summary>
public class RegistrationTests
{
    static WebFactory Web() { var w = new WebFactory(); w.Booking.Respond = _ => FakeBackend.Json(new Gabinety.Web.Api.PageDto<Gabinety.Web.Api.BookingViewDto>([], null)); return w; }

    static Dictionary<string, string> Form(string email = "demo-new@demo.example", string password = "Demo-Dev-Passw0rd!", bool consent = true, string? terms = FakeIdentity.ConsentVersion, string? privacy = FakeIdentity.ConsentVersion)
    {
        var f = new Dictionary<string, string> { ["Model.Email"] = email, ["Model.Password"] = password, ["Model.TermsVersion"] = terms ?? "", ["Model.PrivacyVersion"] = privacy ?? "" };
        if (consent) f["Model.Consent"] = "true";
        return f;
    }

    [Fact]
    public async Task The_page_is_a_real_form_with_labels_the_password_rules_and_the_consent_as_its_own_field()
    {
        await using var web = Web();
        var page = await web.NewBrowser().GetAsync("/rejestracja");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal("Załóż konto", page.TextOf("h1"));
        Assert.Single(page.All("h1"));
        var form = page.One("form[method=post]")!;
        Assert.Equal("Adres e-mail", form.QuerySelector("label[for=register-email]")!.TextContent);
        Assert.Equal("Hasło", form.QuerySelector("label[for=register-password]")!.TextContent);
        Assert.Equal("password", form.QuerySelector("#register-password")!.GetAttribute("type"));
        Assert.Equal("new-password", form.QuerySelector("#register-password")!.GetAttribute("autocomplete"));
        Assert.Equal(["co najmniej 8 znaków", "mała i wielka litera", "cyfra", "znak specjalny"], page.All("#register-password-rules li").Select(li => li.TextContent));
        Assert.Equal("register-password-rules", form.QuerySelector("#register-password")!.GetAttribute("aria-describedby")); // the rules, a visible list, describe the password
        Assert.NotNull(form.QuerySelector("input[name=__RequestVerificationToken]"));
        Assert.Equal("register", form.QuerySelector("input[name=_handler]")!.GetAttribute("value"));
        Assert.Equal("Załóż konto", form.QuerySelector("button[type=submit]")!.TextContent);
        Assert.Equal("noindex,follow", page.One("meta[name=robots]")!.GetAttribute("content"));
        Assert.Contains("Masz już konto?", page.Text);
    }

    [Fact]
    public async Task The_consent_sentence_names_no_internal_version_but_the_versions_travel_with_the_form_F07()
    {
        await using var web = Web();
        var page = await web.NewBrowser().GetAsync("/rejestracja");
        var label = page.One("label.option")!;
        Assert.Equal("Akceptuję Regulamin i Politykę prywatności.", label.TextContent.Trim());
        Assert.DoesNotContain(FakeIdentity.ConsentVersion, page.Text); // not as visible text, not "(wersja dev-1)" either
        Assert.DoesNotContain("wersja", page.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Wersja z dnia 2026-01-01", label.QuerySelectorAll("a")[0].GetAttribute("title")); // available as a tooltip
        Assert.Equal(["/regulamin", "/polityka-prywatnosci"], label.QuerySelectorAll("a").Select(a => a.GetAttribute("href")));
        Assert.Equal(FakeIdentity.ConsentVersion, page.One("input[name='Model.TermsVersion']")!.GetAttribute("value"));
        Assert.Contains("/api/v1/auth/consents/versions", web.Identity.Backend.Requests.Select(r => r.PathAndQuery)); // from the identity service, not from this repository
        Assert.Equal("checkbox", page.One("#register-consent")!.GetAttribute("type")); // journey J2: getByRole("checkbox", { name: /Akceptuję Regulamin/ })
    }

    [Fact]
    public async Task Without_the_consent_versions_the_form_is_not_shown_and_an_alert_says_why()
    {
        await using var web = Web();
        web.Identity.ConsentVersionsDown = true;
        var page = await web.NewBrowser().GetAsync("/rejestracja");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal("Rejestracja jest chwilowo niedostępna. Spróbuj ponownie za chwilę.", page.TextOf("[data-testid=register-unavailable]"));
        Assert.Null(page.One("form[method=post]"));
        Assert.Equal("Załóż konto", page.TextOf("h1"));
    }

    [Fact]
    public async Task An_empty_submit_reports_every_problem_at_once_inline_and_in_a_summary_F06()
    {
        await using var web = Web();
        var r = await web.NewBrowser().PostFormAsync("/rejestracja", Form("", "", consent: false));
        Assert.Equal(HttpStatusCode.OK, r.Status);
        Assert.DoesNotContain(web.Identity.Backend.Requests, x => x.PathAndQuery.Contains("/auth/register"));
        var summary = r.One("[data-testid=form-summary]")!;
        Assert.Equal("alert", summary.GetAttribute("role"));
        Assert.Equal("Popraw 3 pola", summary.QuerySelector("h2")!.TextContent);
        Assert.Equal(["#register-email", "#register-password", "#register-consent"], summary.QuerySelectorAll("a").Select(a => a.GetAttribute("href")));
        foreach (var id in new[] { "register-email", "register-password", "register-consent" })
        {
            Assert.Equal("true", r.One($"#{id}")!.GetAttribute("aria-invalid"));
            Assert.Contains($"{id}-error", r.One($"#{id}")!.GetAttribute("aria-describedby"));
            Assert.NotNull(r.One($"#{id}-error"));
            Assert.StartsWith("Błąd:", r.TextOf($"#{id}-error")); // read by a screen reader, hidden visually
        }
        Assert.Equal("Podaj adres e-mail.", r.TextOf("#register-email-error").Replace("Błąd:", "").Trim());
        Assert.Contains("field--invalid", r.One("#register-email")!.ParentElement!.ClassName);
    }

    [Fact]
    public async Task A_short_password_and_no_consent_are_reported_together_the_case_the_audit_found_F06()
    {
        await using var web = Web();
        var r = await web.NewBrowser().PostFormAsync("/rejestracja", Form(password: "Ab1!", consent: false));
        Assert.Equal("Popraw 2 pola", r.TextOf("[data-testid=form-summary] h2"));
        Assert.Contains("co najmniej 8 znaków", r.TextOf("#register-password-error"));
        Assert.Contains("zaakceptuj Regulamin", r.TextOf("#register-consent-error"));
        Assert.Null(r.One("#register-email-error")); // the valid field stays quiet
        Assert.DoesNotContain("Ab1!", r.Html); // the password is never echoed
        Assert.Equal("demo-new@demo.example", r.One("#register-email")!.GetAttribute("value"));
    }

    [Fact]
    public async Task A_valid_registration_signs_the_user_in_sends_the_shown_versions_and_goes_to_the_target()
    {
        await using var web = Web();
        var browser = web.NewBrowser();
        var r = await browser.PostFormAsync("/rejestracja?redirect=%2Fmoje%2Frezerwacje", Form());
        Assert.Equal(HttpStatusCode.Found, r.Status);
        Assert.Equal("/moje/rezerwacje", r.Location);
        Assert.Contains(r.SetCookies, c => c.StartsWith("gt_session="));
        var body = web.Identity.Backend.Requests.Single(x => x.PathAndQuery == "/api/v1/auth/register").Body!;
        Assert.Contains("\"acceptedTermsVersion\":\"2026-01-01\"", body);
        Assert.Contains("\"acceptedPrivacyVersion\":\"2026-01-01\"", body);
        Assert.Contains("\"locale\":\"pl\"", body);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/moje/rezerwacje")).Status);
    }

    [Theory]
    [InlineData("https://evil.example", "/")] [InlineData("//evil.example", "/")] [InlineData("/rejestracja", "/")]
    public async Task The_return_target_is_filtered(string redirect, string expected) =>
        Assert.Equal(expected, (await Web().NewBrowser().PostFormAsync($"/rejestracja?redirect={Uri.EscapeDataString(redirect)}", Form())).Location);

    [Fact]
    public async Task An_accepted_registration_without_tokens_goes_to_a_real_check_your_inbox_page_and_signs_nobody_in()
    {
        await using var web = Web();
        var browser = web.NewBrowser();
        var r = await browser.PostFormAsync("/rejestracja", Form("pending@demo.example"));
        Assert.Equal(HttpStatusCode.Found, r.Status);
        Assert.Equal("/rejestracja/sprawdz-skrzynke", r.Location);
        Assert.DoesNotContain(r.SetCookies, c => c.StartsWith("gt_session="));
        Assert.DoesNotContain("pending@demo.example", r.Location!); // no address in a URL

        var page = await browser.GetAsync(r.Location!);
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal("Sprawdź skrzynkę", page.TextOf("h1"));
        Assert.Contains("Wysłaliśmy wiadomość z linkiem potwierdzającym", page.TextOf("[data-testid=check-inbox]"));
        Assert.Equal("/logowanie", page.One("main a.btn")!.GetAttribute("href"));
        Assert.Equal("noindex,follow", page.One("meta[name=robots]")!.GetAttribute("content"));
    }

    [Fact]
    public async Task A_refused_registration_says_so_without_saying_why_and_keeps_the_address_but_not_the_password()
    {
        await using var web = Web();
        var r = await web.NewBrowser().PostFormAsync("/rejestracja", Form("taken@demo.example"));
        Assert.Equal(HttpStatusCode.OK, r.Status);
        Assert.Equal("Nie udało się założyć konta. Sprawdź dane i spróbuj ponownie.", r.TextOf("[data-testid=register-error]"));
        Assert.DoesNotContain("Duplicate", r.Html); // nothing of the service's text
        Assert.Equal("taken@demo.example", r.One("#register-email")!.GetAttribute("value"));
        Assert.DoesNotContain("Demo-Dev-Passw0rd!", r.Html);
        Assert.DoesNotContain(r.SetCookies, c => c.StartsWith("gt_session="));
        Assert.Equal(FakeIdentity.ConsentVersion, r.One("input[name='Model.TermsVersion']")!.GetAttribute("value")); // a retry works
    }

    [Theory]
    [InlineData(429, "Zbyt wiele prób. Spróbuj ponownie za minutę.")]
    [InlineData(503, "Rejestracja jest chwilowo niedostępna. Spróbuj ponownie za chwilę.")]
    [InlineData(500, "Rejestracja jest chwilowo niedostępna. Spróbuj ponownie za chwilę.")]
    public async Task A_throttled_or_failing_identity_service_is_an_alert_with_its_own_text(int status, string text)
    {
        await using var web = Web();
        web.Identity.RegisterStatus = status;
        var r = await web.NewBrowser().PostFormAsync("/rejestracja", Form());
        Assert.Equal(HttpStatusCode.OK, r.Status);
        Assert.Equal(text, r.TextOf("[data-testid=register-error]"));
        Assert.Equal("alert", r.One("[data-testid=register-error]")!.GetAttribute("role"));
    }

    [Fact]
    public async Task A_post_without_a_token_is_refused_by_antiforgery()
    {
        await using var web = Web();
        var response = await web.Client().PostAsync("/rejestracja", new FormUrlEncodedContent(Form()));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(web.Identity.Backend.Requests, x => x.PathAndQuery.Contains("/auth/register"));
    }

    [Fact]
    public async Task A_post_from_a_foreign_origin_never_reaches_the_page()
    {
        await using var web = Web();
        var r = await web.NewBrowser().PostFormAsync("/rejestracja", Form(), headers: ("Origin", "https://evil.example"));
        Assert.Equal(HttpStatusCode.Forbidden, r.Status);
        Assert.DoesNotContain(web.Identity.Backend.Requests, x => x.PathAndQuery.Contains("/auth/register"));
    }

    [Fact]
    public async Task A_signed_in_visitor_is_sent_on_and_the_login_link_keeps_the_target()
    {
        await using var web = Web();
        var anon = await web.NewBrowser().GetAsync("/rejestracja?redirect=%2Fmoje%2Frezerwacje");
        Assert.Equal("/logowanie?redirect=%2Fmoje%2Frezerwacje", anon.All("main a").Single(a => a.TextContent == "Zaloguj się").GetAttribute("href"));
        var browser = web.NewBrowser();
        await SignIn.As(web, browser);
        var page = await browser.GetAsync("/rejestracja?redirect=%2Fmoje%2Frezerwacje");
        Assert.Equal("/moje/rezerwacje", page.Location);
    }

    [Fact]
    public async Task The_github_button_keeps_the_target()
    {
        await using var web = Web();
        var page = await web.NewBrowser().GetAsync("/rejestracja?redirect=%2Fmoje%2Frezerwacje");
        Assert.Equal("/oauth/github/start?redirect=%2Fmoje%2Frezerwacje", page.All("main a.btn").Single(a => a.TextContent.Contains("GitHub")).GetAttribute("href"));
    }
}
