using System.Net;
using Gabinety.Web.Api;
using Gabinety.Web.Components.Pages.PractitionerArea;

namespace Gabinety.Web.Tests.PractitionerAreaTests;

/// <summary>The dashboard and onboarding at <c>/gabinet-programisty</c>, through the real host with a stateful fake of the services. Hooks of journey J2 are named in the test names.</summary>
public class DashboardHostTests
{
    static string Clean(string s) => Page.Squash(s.Replace(' ', ' '));

    [Fact]
    public async Task A_visitor_is_sent_to_sign_in_and_comes_back()
    {
        await using var web = new WebFactory();
        var page = await web.NewBrowser().GetAsync("/gabinet-programisty");
        Assert.Equal(HttpStatusCode.Found, page.Status);
        Assert.Equal("/logowanie?redirect=%2Fgabinet-programisty", page.Location);
    }

    [Fact]
    public async Task An_account_with_no_profile_is_onboarded_and_told_that_any_account_may_become_a_practitioner()
    {
        var (web, browser, _) = await PractitionerBrowsers.SignedIn();
        await using var _ = web;
        var page = await browser.GetAsync("/gabinet-programisty");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal("Załóż swój gabinet", page.TextOf("h1"));
        Assert.Single(page.All("h1"));
        Assert.Contains("Każde zalogowane konto może założyć gabinet programisty", page.TextOf("[data-testid=open-to-all]")); // F-10: said on the page
        Assert.Equal(4, page.All("ol.steps li").Count); // P-DASH-2: the steps
        var form = page.One("form[method=post]")!;
        Assert.Equal("Nazwa wyświetlana", form.QuerySelector("label")!.TextContent.Trim()); // J2: getByLabel("Nazwa wyświetlana")
        Assert.Equal("displayName", form.QuerySelector("#displayName")!.GetAttribute("name"));
        Assert.Equal("Załóż gabinet", form.QuerySelector("button[type=submit]")!.TextContent); // J2: getByRole("button", {name: "Załóż gabinet"})
        Assert.NotNull(form.QuerySelector("input[name=__RequestVerificationToken]"));
        Assert.Equal("noindex,nofollow", page.One("meta[name=robots]")!.GetAttribute("content"));
    }

    [Fact]
    public async Task Creating_a_gabinet_posts_the_name_with_the_users_bearer_and_redirects_with_a_flash()
    {
        var (web, browser, world) = await PractitionerBrowsers.SignedIn();
        await using var _ = web;
        var response = await browser.Submit("/gabinet-programisty", "create-profile", [new("displayName", "  Demo Jedyny  ")]);
        Assert.Contains((int)response.Status, new[] { 302, 303 });
        Assert.Equal("/gabinet-programisty?flash=gabinet-created", response.Location);
        Assert.Equal("Demo Jedyny", world.Profile!.DisplayName);
        var post = web.Directory.Requests.Single(r => r.Method == HttpMethod.Post && r.PathAndQuery == "/api/v1/me/practitioner");
        Assert.StartsWith("Bearer ", post.Authorization);
        Assert.Equal("""{"displayName":"Demo Jedyny"}""", post.Body);
        var after = await browser.GetAsync(response.Location!);
        Assert.Equal("Panel programisty", after.TextOf("h1")); // J2: the h1 after creating
        Assert.Equal("Gabinet został założony. Uzupełnij kroki poniżej.", after.TextOf("[data-testid=flash]"));
    }

    [Theory]
    [InlineData("")] [InlineData("a")] [InlineData("   ")]
    public async Task A_name_that_is_too_short_is_refused_inline_before_any_call(string name)
    {
        var (web, browser, world) = await PractitionerBrowsers.SignedIn();
        await using var _ = web;
        var response = await browser.Submit("/gabinet-programisty", "create-profile", [new("displayName", name)]);
        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Null(world.Profile);
        Assert.DoesNotContain(web.Directory.Requests, r => r.Method == HttpMethod.Post);
        var input = response.One("#displayName")!;
        Assert.Equal("true", input.GetAttribute("aria-invalid"));
        Assert.Contains("displayName-error", input.GetAttribute("aria-describedby"));
        Assert.Contains("Nazwa ma mieć od 2 do 80 znaków", response.TextOf("#displayName-error"));
        Assert.StartsWith("Błąd: ", response.TextOf("#displayName-error")); // the hidden word that tells a screen reader what this is
    }

    [Fact]
    public async Task A_draft_shows_its_status_seven_open_steps_and_a_disabled_submit_with_the_reason()
    {
        var (web, browser, _) = await PractitionerBrowsers.SignedIn(w => { w.HaveProfile(); w.EmailVerified = false; });
        await using var _ = web;
        var page = await browser.GetAsync("/gabinet-programisty");
        Assert.Equal("Panel programisty", page.TextOf("h1"));
        Assert.Equal(["Szkic"], page.All(".badge").Select(b => b.TextContent).Where(t => t == "Szkic")); // J2: getByText("Szkic", exact) is one element
        var items = page.All("[data-item]");
        Assert.Equal(7, items.Count);
        Assert.All(items, i => Assert.Contains("Do zrobienia", i.TextContent)); // J2: toContainText("Do zrobienia")
        Assert.Equal("Nagłówek profilu", items[0].QuerySelector("strong")!.TextContent);
        Assert.Equal("Postęp: 0 z 7", page.TextOf("[data-testid=progress]"));
        var submit = page.All("button").Single(b => b.TextContent == "Wyślij do weryfikacji");
        Assert.True(submit.HasAttribute("disabled"));
        Assert.Equal("Uzupełnij jeszcze 7 kroków, aby wysłać profil.", page.TextOf("#submit-help"));
        Assert.Equal("submit-help", submit.GetAttribute("aria-describedby"));
    }

    [Fact]
    public async Task Each_item_of_the_checklist_is_its_own_listitem_whose_name_is_unique_on_the_page_for_the_journey()
    {
        var (web, browser, _) = await PractitionerBrowsers.SignedIn(w => w.HaveProfile());
        await using var _ = web;
        var page = await browser.GetAsync("/gabinet-programisty");
        foreach (var name in new[] { "Nagłówek profilu", "Opis (co najmniej 200 znaków)", "Co najmniej jedna aktywna oferta", "Połączone konto GitHub", "Poprawny numer NIP", "Link do portfolio", "Potwierdzony adres e-mail" })
            Assert.Equal(1, page.All("li").Count(li => li.TextContent.Contains(name))); // getByRole("listitem").filter({hasText}) must match one
        Assert.Equal(1, page.ExactText("Szkic"));
    }

    [Fact]
    public async Task A_complete_profile_ticks_every_step_and_enables_the_submit_that_then_redirects_with_the_flash()
    {
        var (web, browser, world) = await PractitionerBrowsers.SignedIn(w => { w.HaveProfile(); w.CompleteProfile(); });
        await using var _ = web;
        var page = await browser.GetAsync("/gabinet-programisty");
        Assert.All(page.All("[data-item]"), i => Assert.Contains("Gotowe", i.TextContent)); // J2: toContainText("Gotowe")
        Assert.False(page.All("button").Single(b => b.TextContent == "Wyślij do weryfikacji").HasAttribute("disabled"));
        Assert.Contains("Po wysłaniu administrator sprawdzi profil", page.TextOf("#submit-help"));

        var response = await browser.Submit("/gabinet-programisty", "submit-profile");
        Assert.Equal("/gabinet-programisty?flash=profile-submitted", response.Location);
        Assert.Equal("Submitted", world.Profile!.Status);
        var after = await browser.GetAsync(response.Location!);
        Assert.Equal("Profil wysłany. Administrator sprawdzi go i da znać.", after.TextOf("[data-testid=flash]")); // J2 text
        Assert.Equal(1, after.ExactText("Czeka na weryfikację")); // J2: getByText exact, one element
        Assert.DoesNotContain(after.All("button"), b => b.TextContent == "Wyślij do weryfikacji"); // J2: toHaveCount(0)
    }

    [Fact]
    public async Task A_submit_the_service_refuses_names_what_is_missing_in_our_words_and_reads_the_checklist_again()
    {
        var (web, browser, world) = await PractitionerBrowsers.SignedIn(w => { w.HaveProfile(); w.CompleteProfile(); });
        await using var _ = web;
        var response = await browser.Submit("/gabinet-programisty", "submit-profile", afterGet: () => world.EmailVerified = false);
        Assert.Equal(HttpStatusCode.OK, response.Status);
        var alert = response.One("[data-testid=submit-note]")!;
        Assert.Equal("alert", alert.GetAttribute("role"));
        Assert.Equal("Profil nie jest jeszcze kompletny. Brakuje: Potwierdzony adres e-mail.", Clean(alert.TextContent));
        Assert.DoesNotContain("SERVER-", response.Html); // no title, detail or message of the server is ever shown
        Assert.Contains("Do zrobienia", response.One("[data-item=Email]")!.TextContent); // read back, not ticked locally
    }

    [Fact]
    public async Task The_github_link_shows_a_code_then_verifies_it_and_the_dashboard_says_which_account_is_linked()
    {
        var (web, browser, world) = await PractitionerBrowsers.SignedIn(w => w.HaveProfile());
        await using var _ = web;
        var requested = await browser.Submit("/gabinet-programisty", "github-request", [new("githubLogin", "demo-gh")]);
        Assert.Equal(HttpStatusCode.OK, requested.Status);
        Assert.Equal("gabinety-verify-ABCDEFGHJK", requested.TextOf("[data-testid=github-code] code"));
        Assert.False(requested.One("[data-testid=github-code]")!.HasAttribute("hidden"));
        Assert.Equal("Sprawdź kod", requested.One("[data-testid=github-code] button")!.TextContent); // J2

        var code = requested.One("[data-testid=github-code]")!;
        Dictionary<string, string> Posted() => code.QuerySelectorAll("input[type=hidden]").ToDictionary(i => i.GetAttribute("name")!, i => i.GetAttribute("value")!);
        async Task<Page> Verify() => await browser.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/gabinet-programisty") { Content = new FormUrlEncodedContent(Posted()) });

        var notYet = await Verify();
        Assert.Equal(HttpStatusCode.OK, notYet.Status);
        Assert.Contains("Nie znaleźliśmy kodu w opisie profilu GitHub", notYet.TextOf("[data-testid=github-error]"));
        Assert.Equal("gabinety-verify-ABCDEFGHJK", notYet.TextOf("[data-testid=github-code] code")); // still there to paste

        world.GithubBioHasCode = true;
        var linked = await Verify();
        Assert.Equal("/gabinet-programisty", linked.Location);
        var after = await browser.GetAsync("/gabinet-programisty");
        Assert.Equal("Połączono konto GitHub: demo-gh.", after.TextOf("[data-testid=github-linked]")); // J2 text
        Assert.Contains("Gotowe", after.One("[data-item=Github]")!.TextContent);
    }

    [Theory]
    [InlineData("")] [InlineData("zły login!")] [InlineData("-")] [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task A_github_login_that_cannot_exist_is_refused_inline_before_any_call(string login)
    {
        var (web, browser, _) = await PractitionerBrowsers.SignedIn(w => w.HaveProfile());
        await using var _ = web;
        var page = await browser.Submit("/gabinet-programisty", "github-request", [new("githubLogin", login)]);
        if (login == "-") { Assert.Single(web.Directory.Requests, r => r.PathAndQuery.EndsWith("github-link")); return; } // "-" has the allowed alphabet; the service decides the rest
        Assert.DoesNotContain(web.Directory.Requests, r => r.PathAndQuery.EndsWith("github-link"));
        Assert.Contains("Login GitHub to 1 do 39 znaków", page.TextOf("#githubLogin-error"));
        Assert.True(page.One("[data-testid=github-code]")!.HasAttribute("hidden")); // the form exists (Blazor dispatches by name) but shows no code
    }

    static BookingViewDto Booking(string title, int days, string? client = "Demo Klient", string status = "Confirmed") =>
        new(Guid.NewGuid(), status, DateTimeOffset.UtcNow.AddDays(days), DateTimeOffset.UtcNow.AddDays(days).AddHours(1), 60, title, new MoneyDto(18000, "PLN"), new PractitionerRefDto(Guid.NewGuid(), "Demo Programista", "demo-programista"),
            client is null ? null : new ClientRefDto(client));

    [Fact]
    public async Task An_approved_dashboard_has_one_public_callout_with_the_profile_link_and_a_hidden_copy_button_and_no_cards_or_checklist()
    {
        var (web, browser, _) = await PractitionerBrowsers.SignedIn(w => { w.HaveProfile("Approved"); w.CompleteProfile(); w.Rules.Add((1, "09:00", "12:00")); w.PaymentState = "pending"; });
        await using var _ = web;
        var page = await browser.GetAsync("/gabinet-programisty");
        var callout = page.One("[data-testid=public-profile]")!;
        Assert.Contains("callout--success", callout.ClassName);
        Assert.Equal("Twój profil jest publiczny", callout.QuerySelector("h2")!.TextContent);
        Assert.Equal("/gabinet/demo-programista", callout.QuerySelector("a")!.GetAttribute("href"));
        Assert.Equal("Zobacz profil publiczny", callout.QuerySelector("a")!.TextContent);
        var copy = callout.QuerySelector("button")!;
        Assert.Equal(("Skopiuj link", "copy", "/gabinet/demo-programista", "Skopiowano"), (copy.TextContent, copy.GetAttribute("data-enhance"), copy.GetAttribute("data-copy"), copy.GetAttribute("data-done")));
        Assert.True(copy.HasAttribute("hidden")); // enhance.js shows it; without the script it is simply not there
        Assert.Null(page.One("[data-testid=next-steps]")); // the four cards are gone
        Assert.Null(page.One("[data-item]")); // an approved profile has no submit checklist any more
        Assert.Equal(0, page.All(".badge").Count(b => b.TextContent == "Gotowe"));
        Assert.Equal(1, page.ExactText("Zatwierdzony")); // J2: the panel agrees
        var next = page.One("[data-testid=next-step]")!; // the one open step (payments) is still named, with its link
        Assert.Equal("/gabinet-programisty/platnosci", next.QuerySelector("a")!.GetAttribute("href"));
    }

    [Fact]
    public async Task An_approved_dashboard_lists_the_next_five_upcoming_consultations_of_the_practitioner_role_soonest_first()
    {
        var (web, browser, world) = await PractitionerBrowsers.SignedIn(w => { w.HaveProfile("Approved"); w.CompleteProfile(); });
        await using var _ = web;
        for (var i = 7; i >= 1; i--) world.Bookings.Add(Booking($"Konsultacja {i}", i, i == 1 ? null : $"Demo Klient {i}"));
        world.Bookings.Add(Booking("Minione", -3, status: "Completed"));
        world.Bookings.Add(Booking("Odwołane", 2, status: "CancelledByClient"));
        var page = await browser.GetAsync("/gabinet-programisty");
        var rows = page.All("[data-testid=upcoming] [data-booking]");
        Assert.Equal(5, rows.Count);
        Assert.Equal(["Konsultacja 1", "Konsultacja 2", "Konsultacja 3", "Konsultacja 4", "Konsultacja 5"], rows.Select(r => r.QuerySelector("strong")!.TextContent));
        Assert.DoesNotContain("Demo Klient 1", rows[0].TextContent); // no client name in the DTO, no stray separator
        Assert.Contains("Demo Klient 2", rows[1].TextContent);
        var id = rows[1].GetAttribute("data-booking");
        Assert.Equal($"/moje/rezerwacje/{id}?role=practitioner", rows[1].QuerySelector("a")!.GetAttribute("href"));
        Assert.Equal("Szczegóły", rows[1].QuerySelector("a")!.TextContent);
        Assert.Contains(web.Booking.Requests, r => r.PathAndQuery == "/api/v1/me/bookings?role=practitioner");
    }

    [Fact]
    public async Task An_approved_dashboard_with_no_consultations_says_so()
    {
        var (web, browser, _) = await PractitionerBrowsers.SignedIn(w => { w.HaveProfile("Approved"); w.CompleteProfile(); });
        await using var _ = web;
        var page = await browser.GetAsync("/gabinet-programisty");
        Assert.Equal("Nie masz jeszcze zaplanowanych konsultacji.", page.TextOf("[data-testid=upcoming-empty]"));
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task The_consultations_region_fails_soft_with_a_warning_and_a_retry_while_the_rest_of_the_page_stays(bool serviceDown)
    {
        var (web, browser, world) = await PractitionerBrowsers.SignedIn(w => { w.HaveProfile("Approved"); w.CompleteProfile(); w.BookingsFail = !serviceDown; w.BookingDown = serviceDown; });
        await using var _ = web;
        var page = await browser.GetAsync("/gabinet-programisty");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        var warn = page.One("[data-testid=upcoming-failed]")!;
        Assert.Contains("callout--warning", warn.ClassName);
        Assert.Contains("Nie możemy teraz pokazać konsultacji", warn.TextContent);
        Assert.Equal("/gabinet-programisty", warn.QuerySelector("a")!.GetAttribute("href"));
        Assert.Null(page.One("[data-testid=upcoming-empty]"));
        Assert.Equal("Panel programisty", page.TextOf("h1"));
        Assert.NotNull(page.One("[data-testid=public-profile]")); // the rest of the page is there
        Assert.DoesNotContain("SERVER-", page.Html);
    }

    [Fact]
    public async Task A_draft_has_one_checklist_a_progress_element_and_marks_only_the_first_open_row_as_next_with_one_link_per_row()
    {
        var (web, browser, _) = await PractitionerBrowsers.SignedIn(w => { w.HaveProfile(); w.CompleteProfile(github: false); w.EmailVerified = false; });
        await using var _ = web;
        var page = await browser.GetAsync("/gabinet-programisty");
        Assert.Single(page.All("ol.checklist")); // the "Co dalej" cards are merged into it
        Assert.Null(page.One("[data-testid=next-steps]"));
        Assert.DoesNotContain("Co dalej", page.Text);
        var bar = page.One("progress")!;
        Assert.Equal(("5", "7", "Postęp uzupełniania profilu"), (bar.GetAttribute("value"), bar.GetAttribute("max"), bar.GetAttribute("aria-label")));
        Assert.Equal("Postęp: 5 z 7", page.TextOf("[data-testid=progress]")); // the text stays beside the bar
        var github = page.One("[data-item=Github]")!;
        Assert.Contains("Następny krok", github.TextContent);
        Assert.Contains("Do zrobienia", github.TextContent);
        Assert.Equal("#github-title", github.QuerySelector("a")!.GetAttribute("href"));
        Assert.Equal("Uzupełnij", github.QuerySelector("a")!.TextContent);
        Assert.Equal(1, page.All(".badge").Count(b => b.TextContent == "Następny krok"));
        Assert.Contains("Gotowe", page.One("[data-item=Headline]")!.TextContent);
        Assert.Null(page.One("[data-item=Headline] a")); // a done row has no link
        Assert.Null(page.One("[data-item=Email] a")); // the e-mail link is outside the product
        Assert.NotNull(page.One("#github-title")); // the anchor target
    }

    [Fact]
    public async Task While_steps_are_open_the_submit_is_disabled_and_aria_disabled_with_the_reason_in_the_form_and_when_done_it_is_neither()
    {
        var (web, browser, _) = await PractitionerBrowsers.SignedIn(w => { w.HaveProfile(); w.CompleteProfile(github: false); });
        await using var _ = web;
        var page = await browser.GetAsync("/gabinet-programisty");
        var submit = page.All("button").Single(b => b.TextContent == "Wyślij do weryfikacji");
        Assert.True(submit.HasAttribute("disabled"));
        Assert.Equal("true", submit.GetAttribute("aria-disabled"));
        Assert.Equal("Uzupełnij jeszcze 1 krok, aby wysłać profil.", page.TextOf("#submit-help"));
        Assert.Same(submit.ParentElement, page.One("#submit-help")!.ParentElement); // the reason is right next to the button

        var (web2, browser2, _) = await PractitionerBrowsers.SignedIn(w => { w.HaveProfile(); w.CompleteProfile(); });
        await using var _2 = web2;
        var ready = (await browser2.GetAsync("/gabinet-programisty")).All("button").Single(b => b.TextContent == "Wyślij do weryfikacji");
        Assert.False(ready.HasAttribute("disabled"));
        Assert.Null(ready.GetAttribute("aria-disabled"));
    }

    [Fact]
    public async Task A_submitted_profile_shows_the_same_single_checklist_read_only_without_a_next_badge_or_submit()
    {
        var (web, browser, _) = await PractitionerBrowsers.SignedIn(w => { w.HaveProfile("Submitted"); w.CompleteProfile(); });
        await using var _ = web;
        var page = await browser.GetAsync("/gabinet-programisty");
        Assert.Equal(7, page.All("[data-item]").Count);
        Assert.Equal("Postęp: 7 z 7", page.TextOf("[data-testid=progress]"));
        Assert.Equal(0, page.All(".badge").Count(b => b.TextContent == "Następny krok"));
        Assert.DoesNotContain(page.All("button"), b => b.TextContent == "Wyślij do weryfikacji");
        Assert.Equal(1, page.ExactText("Czeka na weryfikację"));
    }

    [Theory]
    [InlineData("none", true)] [InlineData("pending", true)] [InlineData("ready", false)] [InlineData(null, false)]
    public async Task The_payments_banner_shows_only_when_a_real_provider_is_on_and_the_account_is_not_ready(string? state, bool banner)
    {
        var (web, browser, _) = await PractitionerBrowsers.SignedIn(w => { w.HaveProfile("Approved"); w.CompleteProfile(); w.PaymentState = state; });
        await using var _ = web;
        var page = await browser.GetAsync("/gabinet-programisty");
        Assert.Equal(banner, page.One("[data-testid=payments-banner]") is not null);
        if (banner)
        {
            Assert.Contains("Bez gotowego konta płatności nie możesz przyjmować rezerwacji", page.TextOf("[data-testid=payments-banner]"));
            Assert.Equal("/gabinet-programisty/platnosci", page.One("[data-testid=payments-banner] a")!.GetAttribute("href"));
        }
    }

    [Fact]
    public async Task When_booking_is_down_the_dashboard_still_renders_and_shows_no_false_banner()
    {
        var (web, browser, _) = await PractitionerBrowsers.SignedIn(w => { w.HaveProfile("Approved"); w.CompleteProfile(); w.BookingDown = true; });
        await using var _ = web;
        var page = await browser.GetAsync("/gabinet-programisty");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Null(page.One("[data-testid=payments-banner]"));
        Assert.DoesNotContain(PractitionerPaths.Payments, page.One("[data-testid=next-step]")?.InnerHtml ?? ""); // unreadable payments are never offered as the next step
        Assert.NotNull(page.One("[data-testid=upcoming-failed]"));
    }

    [Fact]
    public async Task When_the_directory_is_down_the_page_says_so_with_a_503_and_a_way_to_retry()
    {
        var (web, browser, _) = await PractitionerBrowsers.SignedIn(w => { w.HaveProfile(); w.DirectoryDown = true; });
        await using var _ = web;
        var page = await browser.GetAsync("/gabinet-programisty");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, page.Status);
        var alert = page.One("[data-testid=unavailable]")!;
        Assert.Equal("alert", alert.GetAttribute("role"));
        Assert.Equal("/gabinet-programisty", alert.QuerySelector("a")!.GetAttribute("href"));
        Assert.Equal("Panel programisty", page.TextOf("h1"));
        Assert.DoesNotContain("SERVER-", page.Html);
    }

    [Fact]
    public async Task A_rejected_profile_shows_the_reason_and_can_be_sent_again()
    {
        var (web, browser, world) = await PractitionerBrowsers.SignedIn(w => { w.HaveProfile("Rejected"); w.CompleteProfile(); });
        await using var _ = web;
        world.Profile = world.Profile! with { StatusReason = "Uzupełnij opis profilu" };
        var page = await browser.GetAsync("/gabinet-programisty");
        Assert.Contains("Powód: Uzupełnij opis profilu", page.Text);
        Assert.Contains("Popraw profil zgodnie z powodem", page.Text);
        Assert.False(page.All("button").Single(b => b.TextContent == "Wyślij do weryfikacji").HasAttribute("disabled"));
    }

    [Fact]
    public async Task Text_of_the_profile_is_text_not_markup_and_nothing_of_it_is_logged_by_the_page()
    {
        var (web, browser, world) = await PractitionerBrowsers.SignedIn(w => w.HaveProfile());
        await using var _ = web;
        world.Profile = world.Profile! with { StatusReason = "<img src=x onerror=alert(1)>" };
        var page = await browser.GetAsync("/gabinet-programisty");
        Assert.Empty(page.All("img"));
        Assert.Contains("<img src=x onerror=alert(1)>", page.Text);
    }

    [Fact]
    public async Task A_profile_that_is_already_submitted_has_no_submit_form_and_a_forged_post_is_refused_before_any_call()
    {
        var (web, browser, world) = await PractitionerBrowsers.SignedIn(w => { w.HaveProfile("Submitted"); w.CompleteProfile(); });
        await using var _ = web;
        var page = await browser.GetAsync("/gabinet-programisty");
        var token = page.One("input[name=__RequestVerificationToken]");
        var forged = await browser.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/gabinet-programisty") { Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["_handler"] = "submit-profile", ["__RequestVerificationToken"] = token?.GetAttribute("value") ?? "x" }) });
        Assert.Equal(HttpStatusCode.BadRequest, forged.Status);
        Assert.DoesNotContain(web.Directory.Requests, r => r.PathAndQuery.EndsWith("/submit"));
        Assert.Equal("Submitted", world.Profile!.Status);
    }

    [Fact]
    public async Task A_code_posted_back_with_the_verify_form_is_shown_only_when_it_has_the_shape_of_ours()
    {
        var (web, browser, _) = await PractitionerBrowsers.SignedIn(w => w.HaveProfile());
        await using var _ = web;
        var requested = await browser.Submit("/gabinet-programisty", "github-request", [new("githubLogin", "demo-gh")]);
        var hidden = requested.One("[data-testid=github-code]")!.QuerySelectorAll("input[type=hidden]").ToDictionary(i => i.GetAttribute("name")!, i => i.GetAttribute("value")!);
        hidden["code"] = "<script>alert(1)</script> and some other text";
        var response = await browser.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/gabinet-programisty") { Content = new FormUrlEncodedContent(hidden) });
        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.DoesNotContain("alert(1)", response.Html);
        Assert.True(response.One("[data-testid=github-code]")!.HasAttribute("hidden")); // nothing of the forged text is shown
    }
}
