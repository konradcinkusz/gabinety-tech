using System.Net;
using Gabinety.Web.Tests.Pages;

namespace Gabinety.Web.Tests.Pipeline;

/// <summary>
/// T-11.08: the change-password section of /konto against the stateful fake identity. A change answers a fresh pair (ReissueTokensOnPasswordChange): this browser stays signed in
/// and every other refresh token of the account is revoked. Without a pair the session ends and the sign-in page says so. Every problem of the form is shown at once; nothing is sent while one is open.
/// </summary>
public class ChangePasswordTests
{
    const string NewPassword = "Nowe-Haslo-123!", ChangePath = "/api/v1/auth/change-password";

    static async Task<(WebFactory Web, Browser Browser)> SignedIn()
    {
        var web = new WebFactory();
        var browser = web.NewBrowser();
        await SignIn.As(web, browser, returnTo: "/konto");
        return (web, browser);
    }

    static Dictionary<string, string> Change(string current, string password, string? confirmation = null) => new()
    {
        ["Change.CurrentPassword"] = current,
        ["Change.NewPassword"] = password,
        ["Change.Confirmation"] = confirmation ?? password,
    };

    [Fact]
    public async Task The_section_is_there_for_an_account_with_a_password_and_absent_for_one_that_signs_in_with_GitHub()
    {
        var (web, browser) = await SignedIn();
        await using var __ = web;
        var page = await browser.GetAsync("/konto");
        Assert.Equal("Zmień hasło", page.TextOf("#account-password"));
        Assert.NotNull(page.One("input[name='Change.CurrentPassword'][type=password]"));

        web.Identity.HasPassword = false;
        var github = await browser.GetAsync("/konto");
        Assert.Null(github.One("#account-password"));
        Assert.Null(github.One("input[name='Change.NewPassword']"));
    }

    [Fact]
    public async Task A_correct_change_keeps_this_browser_signed_in_and_writes_the_fresh_pair_into_the_cookie()
    {
        var (web, browser) = await SignedIn();
        await using var __ = web;
        var signedInWith = web.Identity.IssuedRefreshTokens[^1];

        var response = await browser.PostFormAsync("/konto", "change-password", Change(FakeIdentity.Password, NewPassword));

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal("Hasło zostało zmienione. Pozostałe sesje zostały zakończone.", response.TextOf("[data-testid=password-changed]"));
        Assert.Contains(response.SetCookies, c => c.StartsWith("gt_session=") && !c.StartsWith("gt_session=;")); // the rotated session cookie
        Assert.Contains(signedInWith, web.Identity.SupersededRefreshTokens); // the old refresh token is refused from now on
        Assert.StartsWith("Bearer eyJ", web.Identity.Backend.Requests.Last(r => r.PathAndQuery == ChangePath).Authorization);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/konto")).Status); // still signed in
    }

    [Fact]
    public async Task Another_session_of_the_account_loses_its_refresh_token_when_the_password_changes()
    {
        var (web, browser) = await SignedIn();
        await using var __ = web;
        var other = web.NewBrowser();
        await SignIn.As(web, other, returnTo: "/konto");
        var otherRefresh = web.Identity.IssuedRefreshTokens[^1];

        await browser.PostFormAsync("/konto", "change-password", Change(FakeIdentity.Password, NewPassword));

        Assert.Contains(otherRefresh, web.Identity.SupersededRefreshTokens);
    }

    [Fact]
    public async Task A_wrong_current_password_marks_that_field_alone_keeps_the_typed_values_and_changes_nothing()
    {
        var (web, browser) = await SignedIn();
        await using var __ = web;

        var page = await browser.PostFormAsync("/konto", "change-password", Change("Wrong-Passw0rd-xyz", NewPassword));

        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal("Popraw 1 pole", page.TextOf("[data-testid=form-summary] h2"));
        var error = page.All(".field__error");
        Assert.Single(error);
        Assert.Contains("Obecne hasło jest nieprawidłowe.", error[0].TextContent);
        Assert.Equal(NewPassword, page.One("#change-new")!.GetAttribute("value")); // the typed values stay in the form
        Assert.Empty(web.Identity.SupersededRefreshTokens);
        Assert.Null(page.One("[data-testid=password-changed]"));
    }

    [Fact]
    public async Task Every_local_problem_is_shown_at_once_and_nothing_is_sent()
    {
        var (web, browser) = await SignedIn();
        await using var __ = web;

        var page = await browser.PostFormAsync("/konto", "change-password", Change("", "short", "other"));

        Assert.Equal("Popraw 3 pola", page.TextOf("[data-testid=form-summary] h2"));
        Assert.Equal(3, page.All(".field__error").Count);
        Assert.DoesNotContain(web.Identity.Backend.Requests, r => r.PathAndQuery == ChangePath);
    }

    [Fact]
    public async Task A_new_password_the_service_refuses_is_an_error_under_the_new_field_only()
    {
        var (web, browser) = await SignedIn();
        await using var __ = web;

        var page = await browser.PostFormAsync("/konto", "change-password", Change(FakeIdentity.Password, FakeIdentity.RefusedPassword));

        Assert.Equal("Popraw 1 pole", page.TextOf("[data-testid=form-summary] h2"));
        var error = Assert.Single(page.All(".field__error"));
        Assert.Contains("Nowe hasło nie spełnia wymagań. Wybierz inne hasło.", error.TextContent);
        Assert.Equal("change-new-error", error.Id);
        Assert.Empty(web.Identity.SupersededRefreshTokens);
    }

    [Fact]
    public async Task Without_a_fresh_pair_the_session_ends_and_the_sign_in_page_says_the_password_changed()
    {
        var (web, browser) = await SignedIn();
        await using var __ = web;
        web.Identity.ReissueOnPasswordChange = false;

        var response = await browser.PostFormAsync("/konto", "change-password", Change(FakeIdentity.Password, NewPassword));

        Assert.Equal(HttpStatusCode.Found, response.Status);
        Assert.Equal("/logowanie?flash=password-changed", response.Location);
        Assert.Contains(response.SetCookies, c => c.StartsWith("gt_session=;")); // the cookie is deleted
        var afterwards = await browser.GetAsync("/konto");
        Assert.Equal(HttpStatusCode.Found, afterwards.Status);
        Assert.StartsWith("/logowanie", afterwards.Location);
        var signIn = await browser.GetAsync("/logowanie?flash=password-changed");
        Assert.Contains("Hasło zostało zmienione. Zaloguj się nowym hasłem.", signIn.Text);
    }

    [Theory]
    [InlineData(429, "Zbyt wiele prób. Spróbuj ponownie za minutę.")]
    [InlineData(503, "Nie udało się teraz zmienić hasła. Spróbuj ponownie za chwilę.")]
    public async Task A_throttled_or_failing_service_is_said_and_the_session_stays(int status, string message)
    {
        var (web, browser) = await SignedIn();
        await using var __ = web;
        var inner = web.Identity.Backend.Respond;
        web.Identity.Backend.Respond = r => r.RequestUri!.AbsolutePath == ChangePath ? new HttpResponseMessage((HttpStatusCode)status) : inner(r);

        var page = await browser.PostFormAsync("/konto", "change-password", Change(FakeIdentity.Password, NewPassword));

        Assert.Equal(message, page.TextOf("[data-testid=change-error]"));
        Assert.Empty(web.Identity.SupersededRefreshTokens);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/konto")).Status);
    }

    [Fact]
    public async Task The_passwords_are_never_logged()
    {
        var logs = new LogCapture();
        await using var web = new WebFactory().LogTo(logs);
        var browser = web.NewBrowser();
        await SignIn.As(web, browser, returnTo: "/konto");

        await browser.PostFormAsync("/konto", "change-password", Change("Wrong-Passw0rd-xyz", NewPassword));
        await browser.PostFormAsync("/konto", "change-password", Change(FakeIdentity.Password, NewPassword));

        Assert.DoesNotContain(logs.Lines, l => l.Contains("Wrong-Passw0rd-xyz") || l.Contains(NewPassword) || l.Contains(FakeIdentity.Password));
    }
}
