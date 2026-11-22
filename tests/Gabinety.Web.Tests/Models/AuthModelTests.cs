using System.Security.Claims;
using Gabinety.Web.Auth;
using Gabinety.Web.Components.Layout;
using Gabinety.Web.Components.Pages.Account;
using Gabinety.Web.Components.Shared;
using Microsoft.AspNetCore.DataProtection;

namespace Gabinety.Web.Tests.Models;

public class RegisterModelTests
{
    static RegisterModel Ok() => new() { Email = "demo-new@demo.example", Password = "Demo-Dev-Passw0rd!", Consent = true };

    [Fact] public void A_valid_form_has_no_problems() => Assert.False(Ok().Validate().Any);

    [Fact]
    public void An_empty_form_reports_every_field_at_once_F06()
    {
        var p = new RegisterModel().Validate();
        Assert.Equal([RegisterModel.EmailId, RegisterModel.PasswordId, RegisterModel.ConsentId], p.Items.Select(i => i.FieldId));
        Assert.Equal("Popraw 3 pola", p.Heading);
    }

    [Fact]
    public void A_short_password_and_a_missing_consent_are_both_reported_the_case_the_audit_found()
    {
        var m = Ok(); m.Password = "abc12"; m.Consent = false;
        var p = m.Validate();
        Assert.Equal(2, p.Count);
        Assert.Contains("co najmniej 8 znaków", p.For(RegisterModel.PasswordId)!.Message);
        Assert.Contains("Regulamin", p.For(RegisterModel.ConsentId)!.Message);
    }

    [Theory]
    [InlineData("Abcdefg1", false)]       // no special character
    [InlineData("abcdefg1!", false)]      // no upper case
    [InlineData("ABCDEFG1!", false)]      // no lower case
    [InlineData("Abcdefgh!", false)]      // no digit
    [InlineData("Abcdef1!", true)]
    [InlineData("ąęśćż1!ĄĘ", false)]      // Polish letters are not ASCII letters: ASP.NET Identity does not count them either
    [InlineData("Zażółć 1 a!", true)]     // a space is a special character
    public void The_password_rules_are_those_of_authservice(string password, bool strong) => Assert.Equal(strong, RegisterModel.IsStrong(password));

    [Fact]
    public void The_password_message_names_what_is_missing_and_a_short_weak_password_gets_one_combined_message()
    {
        string Msg(string pw) { var m = Ok(); m.Password = pw; return m.Validate().For(RegisterModel.PasswordId)!.Message; }
        Assert.Equal("Hasło musi mieć co najmniej 8 znaków.", Msg("Ab1!"));
        Assert.Equal("Hasło musi zawierać małą i wielką literę, cyfrę i znak specjalny.", Msg("abcdefghij"));
        Assert.Equal("Hasło musi mieć co najmniej 8 znaków oraz zawierać małą i wielką literę, cyfrę i znak specjalny.", Msg("abc"));
        Assert.Equal("Podaj hasło.", Msg(""));
        Assert.Equal("Hasło może mieć najwyżej 200 znaków.", Msg(new string('a', 201)));
    }

    [Theory]
    [InlineData("a@b.c", true)] [InlineData(" demo@demo.example ", true)] [InlineData("a@b", false)] [InlineData("a b@c.d", false)] [InlineData("@b.c", false)] [InlineData("a@@b.c", false)] [InlineData("", false)] [InlineData(null, false)]
    public void The_address_shape_catches_typos_and_nothing_more(string? email, bool valid)
    {
        var m = Ok(); m.Email = email;
        Assert.Equal(valid, m.Validate().For(RegisterModel.EmailId) is null);
    }

    [Fact] public void An_address_longer_than_254_characters_is_refused_with_its_own_message() { var m = Ok(); m.Email = new string('a', 250) + "@b.cd"; Assert.Equal("Adres e-mail może mieć najwyżej 254 znaki.", m.Validate().For(RegisterModel.EmailId)!.Message); }

    [Fact] public void A_field_has_at_most_one_problem_and_the_first_one_wins() { var p = new FormProblems(); p.Add("x", "first"); p.Add("x", "second"); Assert.Equal(1, p.Count); Assert.Equal("first", p.For("x")!.Message); }

    [Theory]
    [InlineData(1, "Popraw 1 pole")] [InlineData(2, "Popraw 2 pola")] [InlineData(4, "Popraw 4 pola")] [InlineData(5, "Popraw 5 pól")] [InlineData(11, "Popraw 11 pól")]
    [InlineData(12, "Popraw 12 pól")] [InlineData(14, "Popraw 14 pól")] [InlineData(22, "Popraw 22 pola")] [InlineData(25, "Popraw 25 pól")]
    public void The_summary_heading_has_the_polish_plural(int count, string expected) => Assert.Equal(expected, Gabinety.Web.Text.Pl.FormErrors.Summary(count));
}

public class LoginAndTwoFactorModelTests
{
    [Fact] public void Both_missing_fields_are_reported_together() { var p = new LoginModel().Validate(); Assert.Equal(["Podaj adres e-mail.", "Podaj hasło."], p.Items.Select(i => i.Message)); Assert.Equal("Popraw 2 pola", p.Heading); }
    [Fact] public void A_filled_form_has_no_problems() => Assert.False(new LoginModel { Email = "a@b.c", Password = "x" }.Validate().Any);

    [Theory]
    [InlineData("123456", "123456")] [InlineData("123 456", "123456")] [InlineData(" 123-456 ", "123456")]
    public void An_authenticator_code_is_normalised_the_way_a_person_types_it(string typed, string sent) => Assert.Equal(sent, new TwoFactorModel { Code = typed }.Normalised(recovery: false));

    [Theory]
    [InlineData("abcde-12345", "abcde-12345")] [InlineData(" abcde 12345 ", "abcde12345")]
    public void A_recovery_code_keeps_its_hyphen_because_it_is_part_of_the_code(string typed, string sent) => Assert.Equal(sent, new TwoFactorModel { Code = typed }.Normalised(recovery: true));

    [Theory] [InlineData(null)] [InlineData("")] [InlineData("  - ")]
    public void An_empty_code_is_a_problem(string? code) => Assert.Equal("Wpisz kod.", new TwoFactorModel { Code = code }.Validate().For(TwoFactorModel.CodeId)!.Message);
}

public class SiteNavigationTests
{
    static ClaimsPrincipal User(params string[] roles) =>
        new(new ClaimsIdentity([new Claim("sub", "u1"), new Claim("email", "demo-client@demo.example"), .. roles.Select(r => new Claim(ClaimTypes.Role, r))], "Cookies", ClaimTypes.Name, ClaimTypes.Role));

    /// <summary>The primary order is a product decision (DESIGN-CONTRACT 5.1, UX-010): Programiści, Jak to działa, Cennik. Zasady rankingu is in the footer, not here.</summary>
    [Fact]
    public void The_primary_links_are_in_order_for_everybody_and_the_ranking_rules_are_in_the_footer()
    {
        Assert.Equal(["/programisci", "/jak-to-dziala", "/cennik"], SiteNavigation.For(null, "/").Links.Select(i => i.Href));
        Assert.Equal(["Programiści", "Jak to działa", "Cennik"], SiteNavigation.For(null, "/").Links.Select(i => i.Label));
        Assert.Equal(["/programisci", "/jak-to-dziala", "/cennik"], SiteNavigation.For(User("User"), "/", practitioner: true).Links.Select(i => i.Href));
        Assert.DoesNotContain(SiteNavigation.For(null, "/").Flat, i => i.Href == "/zasady-rankingu");
        Assert.Equal(["/programisci", "/jak-to-dziala", "/cennik", "/zasady-rankingu"], SiteNavigation.Product.Select(i => i.Href));
    }

    [Fact]
    public void The_practitioner_panel_is_for_practitioners_and_everybody_else_signed_in_is_offered_the_way_in()
    {
        var practitioner = SiteNavigation.For(User("User"), "/", practitioner: true);
        Assert.Contains(practitioner.AccountMenu, i => i.Label == "Panel programisty" && i.Href == "/gabinet-programisty");
        Assert.DoesNotContain(practitioner.AccountMenu, i => i.Label == "Zostań programistą");

        var client = SiteNavigation.For(User("User"), "/");
        Assert.Contains(client.AccountMenu, i => i.Label == "Zostań programistą" && i.Href == "/gabinet-programisty");
        Assert.DoesNotContain(client.AccountMenu, i => i.Label == "Panel programisty");
        Assert.DoesNotContain(client.Flat, i => i.Label == "Panel programisty");
    }

    [Fact]
    public void The_footer_offers_the_way_in_to_a_visitor_and_to_a_client_and_the_panel_to_a_practitioner()
    {
        var visitor = SiteNavigation.Footer(null, practitioner: false);
        Assert.Equal(("/rejestracja", "Zostań programistą"), (visitor.Practitioner.Href, visitor.Practitioner.Label));
        Assert.Equal(("/gabinet-programisty", "Zostań programistą"), (SiteNavigation.Footer(User("User"), false).Practitioner.Href, SiteNavigation.Footer(User("User"), false).Practitioner.Label));
        var member = SiteNavigation.Footer(User("User"), practitioner: true).Practitioner;
        Assert.Equal(("/gabinet-programisty", "Panel programisty"), (member.Href, member.Label));
        Assert.Equal(["/regulamin", "/polityka-prywatnosci"], visitor.Legal.Select(i => i.Href));
    }

    [Theory]
    [InlineData("demo-client@demo.example", "DC")]
    [InlineData("jan.kowalski@example.test", "JK")]
    [InlineData("anna@example.test", "AN")]
    [InlineData("a_b+c@example.test", "AB")]
    [InlineData("x@example.test", "X")]
    [InlineData("@example.test", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("...@example.test", "")]
    public void The_initials_of_the_avatar_come_from_the_e_mail_alone(string? email, string expected) => Assert.Equal(expected, AccountInitials.From(email));

    [Fact]
    public void A_visitor_gets_the_two_buttons_and_no_account_menu()
    {
        var m = SiteNavigation.For(new ClaimsPrincipal(new ClaimsIdentity()), "/");
        Assert.False(m.SignedIn);
        Assert.Equal(("/logowanie", "/rejestracja"), (m.SignIn!.Href, m.Register!.Href));
        Assert.Empty(m.AccountMenu);
        Assert.DoesNotContain(m.Flat, i => i.Href is "/wyloguj" or "/konto" or "/moje/rezerwacje");
    }

    [Fact]
    public void A_client_gets_bookings_the_account_menu_and_sign_out_always_and_no_administration()
    {
        var m = SiteNavigation.For(User("User"), "/");
        Assert.True(m.SignedIn);
        Assert.Equal(["/programisci", "/jak-to-dziala", "/cennik"], m.Links.Select(i => i.Href));
        Assert.Equal(["/moje/rezerwacje", "/konto", "/gabinet-programisty", "/wyloguj"], m.AccountMenu.Select(i => i.Href));
        Assert.Equal("demo-client@demo.example", m.Email);
        Assert.Equal("DC", m.Initials);
        Assert.DoesNotContain(m.Flat, i => i.Href.StartsWith("/admin"));
    }

    [Fact]
    public void A_visitor_has_no_initials_and_a_signed_in_account_with_no_e_mail_has_no_e_mail_text()
    {
        Assert.Equal("", SiteNavigation.For(null, "/").Initials);
        var noEmail = SiteNavigation.For(new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "u1")], "Cookies")), "/");
        Assert.Null(noEmail.Email);
        Assert.Equal("", noEmail.Initials);
    }

    [Theory] [InlineData("Admin")] [InlineData("SuperAdmin")]
    public void An_administrator_also_gets_administration_in_the_account_menu(string role) => Assert.Contains(SiteNavigation.For(User(role), "/").AccountMenu, i => i.Href == "/admin/weryfikacja");

    [Fact] public void Administration_is_not_shown_for_a_look_alike_role() => Assert.DoesNotContain(SiteNavigation.For(User("administrator", "Moderator"), "/").AccountMenu, i => i.Href.StartsWith("/admin"));

    [Theory]
    [InlineData("/programisci", "/programisci", true)] [InlineData("/programisci/audyt-kodu", "/programisci", true)] [InlineData("/programisci/", "/programisci", true)] [InlineData("/PROGRAMISCI", "/programisci", true)]
    [InlineData("/programisci-x", "/programisci", false)] [InlineData("/", "/programisci", false)] [InlineData("/admin/spory", "/admin/weryfikacja", false)]
    public void The_current_section_is_a_path_prefix_at_a_segment_boundary(string path, string href, bool current) => Assert.Equal(current, new NavItem(href, "x").IsCurrent(path));

    [Fact] public void Administration_is_current_on_every_admin_page() { Assert.True(SiteNavigation.Admin.IsCurrent("/admin/spory")); Assert.True(SiteNavigation.Admin.IsCurrent("/admin")); Assert.False(SiteNavigation.Admin.IsCurrent("/administracja")); }
}

public class TwoFactorChallengeTests
{
    static (TwoFactorChallenge Challenge, TestClock Clock) Rig(IDataProtectionProvider? provider = null)
    {
        var clock = TestClock.Default();
        return (new TwoFactorChallenge(provider ?? new EphemeralDataProtectionProvider(), clock), clock);
    }

    [Fact]
    public void A_sealed_challenge_opens_with_its_target_and_is_not_readable()
    {
        var (c, _) = Rig();
        var sealedValue = c.Seal("challenge-secret-token", "/moje/rezerwacje?role=practitioner");
        Assert.DoesNotContain("challenge-secret", sealedValue);
        var pending = c.Unseal(sealedValue)!;
        Assert.Equal(("challenge-secret-token", "/moje/rezerwacje?role=practitioner"), (pending.ChallengeToken, pending.Redirect));
    }

    [Fact] public void A_foreign_target_is_filtered_when_sealing() { var (c, _) = Rig(); Assert.Equal("/", c.Unseal(c.Seal("t", "https://evil.example"))!.Redirect); }

    [Fact]
    public void It_expires_with_the_challenge_after_five_minutes()
    {
        var (c, clock) = Rig();
        var v = c.Seal("t", "/");
        clock.Advance(TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(1));
        Assert.NotNull(c.Unseal(v));
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Null(c.Unseal(v));
    }

    [Theory] [InlineData(null)] [InlineData("")] [InlineData("garbage")] [InlineData("CfDJ8AAAA")]
    public void Garbage_is_no_challenge(string? raw) => Assert.Null(Rig().Challenge.Unseal(raw));

    [Fact] public void A_value_sealed_for_another_purpose_or_by_another_key_ring_is_no_challenge()
    {
        var (c, _) = Rig();
        var other = new EphemeralDataProtectionProvider().CreateProtector("gabinety.web.two-factor-challenge.v1").Protect("t\n/");
        Assert.Null(c.Unseal(other));
        var wrongPurpose = c.GetType() == typeof(TwoFactorChallenge) ? new EphemeralDataProtectionProvider().CreateProtector("something-else").Protect("t\n/") : "";
        Assert.Null(c.Unseal(wrongPurpose));
    }
}
