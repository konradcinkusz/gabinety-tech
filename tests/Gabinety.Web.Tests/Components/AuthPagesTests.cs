using Bunit;
using Gabinety.Web.Api;
using Gabinety.Web.Auth;
using Gabinety.Web.Components.Pages.Account;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Gabinety.Web.Tests.Rendering;

/// <summary>
/// The sign-in and registration pages with bUnit (UX-016): the centred card, one heading, the booking context line, the live region that exists before any error, the show/hide control
/// that only a script enables, the password rules as a list, the consent as its own field. No host and no backend answer: the services are only injected, rendering never calls them.
/// </summary>
public class AuthPagesTests : BunitContext
{
    public AuthPagesTests()
    {
        Services.AddSingleton(new SignInService(null!, null!, null!, null!, null!, null!, TimeProvider.System));
        Services.AddSingleton(new IdentityClient(
            new HttpClient(new FakeBackend { Respond = _ => FakeBackend.Json(new { terms = FakeIdentity.ConsentVersion, privacy = FakeIdentity.ConsentVersion }) }) { BaseAddress = new Uri("http://auth.test") },
            new VisitorAddressAccessor(new HttpContextAccessor())));
        Services.AddSingleton(new TwoFactorChallenge(new EphemeralDataProtectionProvider(), TimeProvider.System));
    }

    /// <summary>Renders a page at <paramref name="url"/>, so its query parameters are supplied the way the host supplies them.</summary>
    IRenderedComponent<CascadingValue<HttpContext>> Page<T>(string url) where T : IComponent
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo(url);
        return Render<CascadingValue<HttpContext>>(p => p.Add(c => c.Value, new DefaultHttpContext()).AddChildContent<T>(_ => { }));
    }

    /// <summary>The registration reads the consent versions in its initialisation; the form appears once they are in.</summary>
    IRenderedComponent<CascadingValue<HttpContext>> Registration(string url = "http://localhost/rejestracja")
    {
        var page = Page<Register>(url);
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("#register-password")));
        return page;
    }

    [Fact]
    public void Sign_in_is_one_centred_card_with_one_heading_and_the_fields_its_hooks_name()
    {
        var cut = Page<Login>("http://localhost/logowanie");
        Assert.Equal(["Logowanie"], cut.FindAll("h1").Select(h => h.TextContent));
        Assert.NotNull(cut.Find(".container--form > .card"));
        Assert.Equal("Adres e-mail", cut.Find("label[for=login-email]").TextContent);
        Assert.Equal("Hasło", cut.Find("label[for=login-password]").TextContent);
        var email = cut.Find("#login-email");
        Assert.Equal(("Model.Email", "username", "email"), (email.GetAttribute("name"), email.GetAttribute("autocomplete"), email.GetAttribute("inputmode")));
        var password = cut.Find("#login-password");
        Assert.Equal(("Model.Password", "current-password", "password"), (password.GetAttribute("name"), password.GetAttribute("autocomplete"), password.GetAttribute("type")));
        Assert.Equal("Zaloguj się", cut.Find("button[type=submit]").TextContent);
    }

    [Fact]
    public void The_show_password_control_is_present_hidden_and_never_a_submit_button()
    {
        // Progressive enhancement: without a script the attribute keeps the control out of sight and out of the tab order; a later script removes it.
        var reveal = Page<Login>("http://localhost/logowanie").Find("button[data-enhance=reveal]");
        Assert.True(reveal.HasAttribute("hidden"));
        Assert.Equal(("button", "#login-password", "Pokaż hasło"), (reveal.GetAttribute("type"), reveal.GetAttribute("data-target"), reveal.TextContent));
        Assert.Contains("btn--ghost", reveal.ClassName);

        var registration = Registration().Find("button[data-enhance=reveal]");
        Assert.True(registration.HasAttribute("hidden"));
        Assert.Equal(("button", "#register-password"), (registration.GetAttribute("type"), registration.GetAttribute("data-target")));
    }

    [Fact]
    public void A_sign_in_that_continues_a_booking_says_so_and_does_not_print_the_query()
    {
        var cut = Page<Login>("http://localhost/logowanie?redirect=%2Frezerwacja%2Fabc%3Fstart%3D2026-12-01T09%3A00Z%26gabinet%3Ddemo-anna");
        var context = cut.Find("[data-testid=booking-context]");
        Assert.Equal("Zaloguj się, aby dokończyć rezerwację.", context.TextContent);
        Assert.Contains("callout--info", context.ClassName);
        Assert.DoesNotContain("2026-12-01", cut.Find(".card").TextContent); // the target is read, the raw query is never shown
        Assert.Empty(Page<Login>("http://localhost/logowanie?redirect=%2Fmoje%2Frezerwacje").FindAll("[data-testid=booking-context]"));
    }

    [Fact]
    public void A_registration_that_continues_a_booking_says_so()
    {
        var cut = Registration("http://localhost/rejestracja?redirect=%2Frezerwacja%2Fabc");
        Assert.Equal("Załóż konto, aby dokończyć rezerwację.", cut.Find("[data-testid=booking-context]").TextContent);
        Assert.Empty(Registration().FindAll("[data-testid=booking-context]"));
    }

    [Fact]
    public void The_login_error_region_exists_before_any_error_and_holds_the_message_when_there_is_one()
    {
        var quiet = Page<Login>("http://localhost/logowanie");
        var region = quiet.Find("div[role=alert]");
        Assert.Empty(region.Children);
        Assert.Empty(quiet.FindAll("[data-testid=login-error]"));

        var failed = Page<Login>("http://localhost/logowanie?error=github_failed");
        Assert.Equal("Logowanie przez GitHub nie powiodło się. Spróbuj ponownie lub użyj hasła.", failed.Find("div[role=alert] > [data-testid=login-error]").TextContent);
    }

    [Fact]
    public void The_github_button_keeps_its_text_and_carries_a_decorative_mark_in_the_text_colour()
    {
        var button = Page<Login>("http://localhost/logowanie").Find("a.btn--secondary");
        Assert.Equal("Zaloguj się przez GitHub", button.TextContent);
        Assert.Equal("/oauth/github/start", button.GetAttribute("href"));
        var mark = button.QuerySelector("svg")!;
        Assert.Equal(("true", "currentColor", "false"), (mark.GetAttribute("aria-hidden"), mark.GetAttribute("fill"), mark.GetAttribute("focusable")));
        Assert.False(mark.HasAttribute("style")); // no colour of its own
    }

    [Fact]
    public void Registration_shows_the_password_rules_as_a_list_that_describes_the_password()
    {
        var cut = Registration();
        Assert.Equal(["Załóż konto"], cut.FindAll("h1").Select(h => h.TextContent));
        Assert.NotNull(cut.Find(".container--form > .card"));
        Assert.Equal(["co najmniej 8 znaków", "mała i wielka litera", "cyfra", "znak specjalny"], cut.FindAll("#register-password-rules li").Select(li => li.TextContent));
        Assert.Equal("register-password-rules", cut.Find("#register-password").GetAttribute("aria-describedby"));
    }

    [Fact]
    public void The_check_inbox_logout_and_second_step_pages_are_one_centred_card_with_one_heading()
    {
        var inbox = Page<CheckInbox>("http://localhost/rejestracja/sprawdz-skrzynke");
        Assert.Equal(["Sprawdź skrzynkę"], inbox.FindAll("h1").Select(h => h.TextContent));
        Assert.NotNull(inbox.Find(".container--form > .card [data-testid=check-inbox][role=status]"));

        var logout = Page<Logout>("http://localhost/wyloguj");
        Assert.Equal(["Wylogowanie"], logout.FindAll("h1").Select(h => h.TextContent));
        Assert.Equal("Wyloguj się", logout.Find(".card button[type=submit]").TextContent);

        var second = Page<TwoFactor>("http://localhost/logowanie/kod"); // without a challenge the page asks for a new sign-in; its markup is still one card
        Assert.Equal(["Kod weryfikacyjny"], second.FindAll("h1").Select(h => h.TextContent));
        Assert.NotNull(second.Find(".container--form > .card"));
    }

    [Fact]
    public void The_consent_is_its_own_field_with_a_44_px_option_row_apart_from_the_submit_row()
    {
        var cut = Registration();
        var option = cut.Find("label.option");
        Assert.Equal("Akceptuję Regulamin i Politykę prywatności.", option.TextContent.Trim());
        Assert.Equal("checkbox", option.QuerySelector("input")!.GetAttribute("type"));
        Assert.True(option.ParentElement!.ClassList.Contains("field")); // its own field, with room for its own error
        var submitRow = cut.Find("button[type=submit]").ParentElement!;
        Assert.True(submitRow.ClassList.Contains("form-actions"));
        Assert.NotSame(option.ParentElement, submitRow);
    }
}
