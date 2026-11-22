using System.Net;
using System.Web;

namespace Gabinety.Web.Tests.Pipeline;

/// <summary>GitHub sign-in with the gt_oauth_state scheme of PR #118 on the Blazor host, and the registration/2FA/202 states as the plumbing returns them.</summary>
public class OAuthTests
{
    static WebFactory Web() { var w = new WebFactory(); w.Booking.Respond = _ => FakeBackend.Json(new Gabinety.Web.Api.PageDto<Gabinety.Web.Api.BookingViewDto>([], null)); return w; }

    static async Task<(Page Start, string State, Uri Target)> Start(Browser b, string query = "")
    {
        var start = await b.GetAsync("/oauth/github/start" + query, ("Host", "evil.example")); // a forged Host must not reach returnUrl
        var target = new Uri(start.Location!);
        return (start, HttpUtility.ParseQueryString(new Uri(HttpUtility.ParseQueryString(target.Query)["returnUrl"]!).Query)["state"]!, target);
    }

    [Fact]
    public async Task Start_sets_the_state_cookie_and_sends_the_browser_to_authservice_with_a_returnUrl_from_the_site_not_the_request_host()
    {
        await using var web = Web();
        var browser = web.NewBrowser();
        var (start, state, target) = await Start(browser, "?redirect=%2Fmoje%2Frezerwacje");
        Assert.Equal(HttpStatusCode.Found, start.Status);
        Assert.Equal("http://auth.test/api/v1/external-auth/login", target.GetLeftPart(UriPartial.Path));
        var q = HttpUtility.ParseQueryString(target.Query);
        Assert.Equal("GitHub", q["provider"]);
        var returnUrl = new Uri(q["returnUrl"]!);
        Assert.Equal("http://localhost/oauth/callback", returnUrl.GetLeftPart(UriPartial.Path)); // Site:BaseUrl, never evil.example
        Assert.Equal("/moje/rezerwacje", HttpUtility.ParseQueryString(returnUrl.Query)["redirect"]);
        Assert.True(state.Length >= 43);
        var cookie = Assert.Single(start.SetCookies);
        Assert.StartsWith($"gt_oauth_state={state}", cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max-age=600", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no-store", start.Header("Cache-Control"));
    }

    [Fact]
    public async Task Start_filters_the_return_target_and_gives_every_visit_its_own_state()
    {
        await using var web = Web();
        var (_, s1, t1) = await Start(web.NewBrowser(), "?redirect=%2F%2Fevil.example");
        var (_, s2, _) = await Start(web.NewBrowser());
        Assert.NotEqual(s1, s2);
        Assert.DoesNotContain("redirect", HttpUtility.ParseQueryString(new Uri(HttpUtility.ParseQueryString(t1.Query)["returnUrl"]!).Query).AllKeys!);
    }

    [Fact]
    public async Task Without_an_identity_address_start_ends_as_a_failed_github_sign_in()
    {
        await using var web = Web().With("Backend:Auth", "");
        var start = await web.NewBrowser().GetAsync("/oauth/github/start");
        Assert.Equal("/logowanie?error=github_failed", start.Location);
        Assert.Empty(start.SetCookies);
    }

    [Fact]
    public async Task The_public_identity_address_wins_for_the_browser_redirect()
    {
        await using var web = Web().With("Identity:PublicUrl", "https://auth.gabinety.example/");
        var start = await web.NewBrowser().GetAsync("/oauth/github/start");
        Assert.StartsWith("https://auth.gabinety.example/api/v1/external-auth/login?provider=GitHub", start.Location);
    }

    [Fact]
    public async Task A_callback_with_the_matching_state_exchanges_the_code_signs_in_and_deletes_the_state()
    {
        await using var web = Web();
        var browser = web.NewBrowser();
        var (_, state, _) = await Start(browser, "?redirect=%2Fmoje%2Frezerwacje");
        web.Identity.Backend.Respond = r => r.RequestUri!.AbsolutePath == "/api/v1/external-auth/exchange"
            ? FakeBackend.Json(new { accessToken = web.Identity.Issue(email: "gh@demo.example"), refreshToken = "r", expiresIn = 3600 })
            : FakeBackend.Raw(web.Identity.Jwks());
        var cb = await browser.GetAsync($"/oauth/callback?code=one-time&state={state}&redirect=%2Fmoje%2Frezerwacje");
        Assert.Equal(("/moje/rezerwacje", true), (cb.Location, browser.Cookies.ContainsKey("gt_session")));
        Assert.False(browser.Cookies.ContainsKey("gt_oauth_state")); // single use
        Assert.Contains(cb.SetCookies, c => c.StartsWith("gt_oauth_state=;") && c.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("\"code\":\"one-time\"", web.Identity.Backend.Requests.Single(r => r.PathAndQuery.Contains("exchange")).Body);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/moje/rezerwacje")).Status);
    }

    [Theory]
    [InlineData("wrong")] [InlineData("")] [InlineData(null)]
    public async Task A_callback_whose_state_does_not_match_the_cookie_is_refused_without_calling_the_exchange(string? sent)
    {
        await using var web = Web();
        var browser = web.NewBrowser();
        await Start(browser);
        var cb = await browser.GetAsync($"/oauth/callback?code=c{(sent is null ? "" : $"&state={sent}")}");
        Assert.Equal("/logowanie?error=github_failed", cb.Location);
        Assert.DoesNotContain(web.Identity.Backend.Requests, r => r.PathAndQuery.Contains("exchange"));
        Assert.False(browser.Cookies.ContainsKey("gt_session"));
        Assert.False(browser.Cookies.ContainsKey("gt_oauth_state")); // deleted on every outcome
    }

    [Fact]
    public async Task A_callback_in_another_browser_without_the_cookie_is_refused_login_CSRF()
    {
        await using var web = Web();
        var (_, state, _) = await Start(web.NewBrowser()); // the attacker's browser started the flow
        var victim = web.NewBrowser();
        var cb = await victim.GetAsync($"/oauth/callback?code=attackers-code&state={state}");
        Assert.Equal("/logowanie?error=github_failed", cb.Location);
        Assert.DoesNotContain(web.Identity.Backend.Requests, r => r.PathAndQuery.Contains("exchange"));
    }

    [Fact]
    public async Task A_missing_code_or_a_refused_exchange_or_a_challenge_are_each_a_login_error_and_the_open_redirect_is_filtered()
    {
        await using var web = Web();
        var browser = web.NewBrowser();
        var (_, state, _) = await Start(browser);
        Assert.Equal("/logowanie?error=github_failed", (await browser.GetAsync($"/oauth/callback?state={state}")).Location);

        var (_, s2, _) = await Start(browser);
        web.Identity.Backend.Respond = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized);
        Assert.Equal("/logowanie?error=github_failed", (await browser.GetAsync($"/oauth/callback?code=used&state={s2}")).Location);

        var (_, s3, _) = await Start(browser);
        web.Identity.Backend.Respond = _ => FakeBackend.Raw("{\"requiresTwoFactor\":true,\"challengeToken\":\"x\",\"expiresIn\":300}");
        var challenge = await browser.GetAsync($"/oauth/callback?code=c&state={s3}");
        Assert.Equal("/logowanie/kod", challenge.Location); // a real second step, not an error
        Assert.Contains(challenge.SetCookies, c => c.StartsWith("gt_2fa=") && c.Contains("httponly", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(challenge.SetCookies, c => c.StartsWith("gt_session="));

        var (_, s4, _) = await Start(browser);
        web.Identity.Backend.Respond = r => r.RequestUri!.AbsolutePath.Contains("exchange") ? FakeBackend.Json(new { accessToken = web.Identity.Issue(), refreshToken = "r", expiresIn = 3600 }) : FakeBackend.Raw(web.Identity.Jwks());
        Assert.Equal("/", (await browser.GetAsync($"/oauth/callback?code=c&state={s4}&redirect=https%3A%2F%2Fevil.example")).Location);
    }

    [Fact]
    public async Task Sign_in_states_unverified_e_mail_is_a_message_and_two_factor_is_a_second_step()
    {
        await using var web = Web();
        web.Identity.Backend.Respond = r => r.RequestUri!.AbsolutePath == "/api/v1/auth/login" ? FakeBackend.Raw("{\"emailVerificationRequired\":true}", HttpStatusCode.Forbidden) : FakeBackend.Raw(web.Identity.Jwks());
        var r1 = await web.NewBrowser().PostFormAsync("/logowanie", new Dictionary<string, string> { ["Model.Email"] = "a@demo.example", ["Model.Password"] = "x" });
        Assert.Contains("Potwierdź adres e-mail", r1.TextOf("[role=alert]"));
        web.Identity.Backend.Respond = _ => FakeBackend.Raw("{\"requiresTwoFactor\":true,\"challengeToken\":\"x\",\"expiresIn\":300}");
        var r2 = await web.NewBrowser().PostFormAsync("/logowanie", new Dictionary<string, string> { ["Model.Email"] = "a@demo.example", ["Model.Password"] = "x" });
        Assert.Equal("/logowanie/kod", r2.Location); // the code is asked for on its own page (TwoFactorTests)
        Assert.DoesNotContain(r2.SetCookies, c => c.StartsWith("gt_session="));
    }
}
