using System.Net;
using System.Text.Json;
using Gabinety.Web.Api;
using Gabinety.Web.Components.Pages.Account;

namespace Gabinety.Web.Tests.Pipeline;

/// <summary>/konto and /konto/export against the real host with fake backends: F-02 (the page works), F-09 (h1 and the e-mail even when the identity service is down), the export, the deletion.</summary>
public class AccountPageTests
{
    static async Task<(WebFactory Web, Browser Browser)> SignedIn()
    {
        var web = new WebFactory();
        var browser = web.NewBrowser();
        await SignIn.As(web, browser, returnTo: "/konto");
        return (web, browser);
    }

    [Fact]
    public async Task A_visitor_is_sent_to_sign_in_and_the_export_too()
    {
        await using var web = new WebFactory();
        var page = await web.NewBrowser().GetAsync("/konto");
        Assert.Equal(HttpStatusCode.Found, page.Status);
        Assert.Equal("/logowanie?redirect=%2Fkonto", page.Location);
        var export = await web.NewBrowser().GetAsync("/konto/export");
        Assert.Equal(HttpStatusCode.Found, export.Status);
        Assert.StartsWith("/logowanie", export.Location);
        Assert.DoesNotContain(web.Identity.Backend.Requests, r => r.PathAndQuery.Contains("/auth/export"));
    }

    [Fact]
    public async Task The_page_shows_h1_email_verification_consents_without_internal_versions_and_no_token()
    {
        var (web, browser) = await SignedIn();
        await using var __ = web;
        web.Identity.EmailConfirmed = false;
        web.Identity.TermsAccepted = false;
        var page = await browser.GetAsync("/konto");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Single(page.All("h1"));
        Assert.Equal("Konto", page.TextOf("h1"));
        Assert.Equal("demo-client@demo.example", page.TextOf("[data-testid=account-email]"));
        Assert.Contains("niepotwierdzony", page.TextOf("[data-testid=account-email-state]"));
        var consents = page.All("[data-testid=consents] li");
        Assert.Equal("Regulamin: wymaga ponownej akceptacji", consents[0].QuerySelector("span")!.TextContent);
        Assert.Contains("Zaktualizowaliśmy regulamin.", consents[0].TextContent); // a pending consent says why, in plain Polish
        Assert.Equal("Polityka prywatności: zaakceptowana", consents[1].QuerySelector("span")!.TextContent);
        Assert.DoesNotContain("Zaktualizowaliśmy", consents[1].TextContent); // an accepted one needs no explanation
        Assert.DoesNotContain("2026-01-01", page.Text); // F-07 spirit: no document versions as text
        Assert.Equal("Wersja dokumentu: 2026-01-01", consents[0].GetAttribute("title")); // a pending consent shows the REQUIRED version, in a title
        Assert.Equal("/konto/export", page.One("a[href='/konto/export']")!.GetAttribute("href"));
        Assert.NotNull(page.One("a[href='/wyloguj']"));
        Assert.DoesNotContain("SECRET", page.Html);
        Assert.DoesNotContain("eyJ", page.Html);
        Assert.All(web.Identity.Backend.Requests.Where(r => r.PathAndQuery is "/api/v1/auth/me" or "/api/v1/auth/consents"), r => Assert.StartsWith("Bearer eyJ", r.Authorization));
    }

    [Fact]
    public async Task The_e_mail_state_is_a_badge_next_to_the_address_not_a_full_width_callout()
    {
        var (web, browser) = await SignedIn();
        await using var __ = web;
        var verified = await browser.GetAsync("/konto");
        var badge = verified.One("[data-testid=account-email-state]")!;
        Assert.Equal("span", badge.TagName.ToLowerInvariant());
        Assert.Contains("badge badge--success", badge.GetAttribute("class"));
        Assert.Equal("Adres e-mail potwierdzony", badge.TextContent.Trim());
        Assert.Equal(verified.One("[data-testid=account-email]")!.ParentElement, badge.ParentElement); // the same line as the address
        Assert.Null(verified.One(".callout--success")); // the old full-width green callout is gone
        Assert.Null(verified.One(".account > .card > p.muted")); // no hint for a confirmed address

        web.Identity.EmailConfirmed = false;
        var unverified = await browser.GetAsync("/konto");
        Assert.Contains("badge--warning", unverified.One("[data-testid=account-email-state]")!.GetAttribute("class"));
        Assert.Contains("Sprawdź skrzynkę", unverified.TextOf(".account > .card > p.muted"));
    }

    [Fact]
    public async Task The_deletion_is_a_collapsed_details_with_the_existing_form_and_no_script()
    {
        var (web, browser) = await SignedIn();
        await using var __ = web;
        var page = await browser.GetAsync("/konto");
        var details = page.One("details.account__danger")!;
        Assert.Null(details.GetAttribute("open")); // collapsed by default
        Assert.Equal("Usuń konto", page.TextOf("details.account__danger > summary"));
        var form = page.One("details.account__danger form[method=post]")!; // the form is inside the details, so it works without script
        Assert.NotNull(form.QuerySelector("input[name='__RequestVerificationToken']")); // antiforgery is still on the form
        Assert.NotNull(form.QuerySelector("input[name='Delete.Confirm']"));
        Assert.NotNull(form.QuerySelector("input[name='Delete.Password']"));
        Assert.NotNull(form.QuerySelector("button[type=submit]"));
        Assert.Equal("Usuń konto", form.QuerySelector("button[type=submit]")!.TextContent.Trim());
        Assert.Single(page.All("h1"));
        Assert.Empty(page.All("form details")); // no nested forms
    }

    [Fact]
    public async Task A_failed_deletion_reopens_the_details_so_the_message_is_seen()
    {
        var (web, browser) = await SignedIn();
        await using var __ = web;
        var page = await browser.PostFormAsync("/konto", Delete("", true));
        Assert.NotNull(page.One("details.account__danger[open]"));
        Assert.Equal("Podaj hasło, aby potwierdzić usunięcie konta.", page.TextOf("details[open] [data-testid=delete-error]"));
    }

    [Fact]
    public async Task When_the_account_data_is_down_the_page_still_has_h1_email_and_sign_out_F09()
    {
        var (web, browser) = await SignedIn();
        await using var __ = web;
        web.Identity.AccountStatus = 503;
        var page = await browser.GetAsync("/konto");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal("Konto", page.TextOf("h1"));
        Assert.Equal("demo-client@demo.example", page.TextOf("[data-testid=account-email]"));
        Assert.Equal("alert", page.One("[data-testid=unavailable]")!.GetAttribute("role"));
        Assert.Equal("Dane konta są chwilowo niedostępne. Spróbuj ponownie za chwilę.", page.TextOf("[data-testid=unavailable]"));
        Assert.NotNull(page.One("a[href='/wyloguj']"));
        Assert.Null(page.One("form[method=post] button.btn--danger")); // nothing to delete blindly
    }

    [Fact]
    public async Task Accepting_the_current_versions_posts_the_consents_and_reloads()
    {
        var (web, browser) = await SignedIn();
        await using var __ = web;
        web.Identity.PrivacyAccepted = false;
        var page = await browser.GetAsync("/konto");
        Assert.Equal("Zaakceptuj aktualne wersje", page.TextOf("form button"));
        var response = await browser.PostFormAsync("/konto", new Dictionary<string, string>());
        Assert.Equal(HttpStatusCode.Found, response.Status);
        Assert.Equal("/konto", response.Location);
        Assert.Equal(1, web.Identity.ConsentPosts);
        Assert.DoesNotContain("Zaakceptuj aktualne wersje", (await browser.GetAsync("/konto")).Text);
    }

    [Fact]
    public async Task The_export_is_an_attachment_that_is_never_cached_and_passes_the_services_body_through()
    {
        var (web, browser) = await SignedIn();
        await using var __ = web;
        var response = await browser.GetAsync("/konto/export");
        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Contains("attachment", response.Header("Content-Disposition"));
        Assert.Contains("gabinety-tech-moje-dane.json", response.Header("Content-Disposition"));
        Assert.Contains("no-store", response.Header("Cache-Control"));
        Assert.StartsWith("application/json", response.Header("Content-Type"));
        Assert.Equal("demo-client@demo.example", JsonDocument.Parse(response.Html).RootElement.GetProperty("profile").GetProperty("email").GetString());
        Assert.StartsWith("Bearer eyJ", web.Identity.Backend.Requests.Last(r => r.PathAndQuery == "/api/v1/auth/export").Authorization);
    }

    [Theory]
    [InlineData(503, 502)] [InlineData(500, 502)] [InlineData(401, 401)]
    public async Task The_export_says_why_it_failed_without_the_services_text(int service, int expected)
    {
        var (web, browser) = await SignedIn();
        await using var __ = web;
        web.Identity.AccountStatus = service;
        var response = await browser.GetAsync("/konto/export");
        Assert.Equal(expected, (int)response.Status);
        Assert.Contains("no-store", response.Header("Cache-Control"));
        Assert.DoesNotContain("attachment", response.Header("Content-Disposition") ?? ""); // never a file
    }

    [Fact]
    public async Task An_identity_service_that_does_not_answer_is_a_503_for_the_export()
    {
        var (web, browser) = await SignedIn();
        await using var __ = web;
        web.Identity.Down = true;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await browser.GetAsync("/konto/export")).Status);
    }

    static Dictionary<string, string> Delete(string? password, bool confirm)
    {
        var f = new Dictionary<string, string>();
        if (password is not null) f["Delete.Password"] = password;
        if (confirm) f["Delete.Confirm"] = "true";
        return f;
    }

    [Fact]
    public async Task Deleting_needs_the_confirmation_and_the_password_before_anything_is_sent()
    {
        var (web, browser) = await SignedIn();
        await using var __ = web;
        var noConfirm = await browser.PostFormAsync("/konto", Delete(FakeIdentity.Password, false));
        Assert.Contains("Zaznacz potwierdzenie, aby usunąć konto.", noConfirm.TextOf("[data-testid=delete-error]"));
        var noPassword = await browser.PostFormAsync("/konto", Delete("", true));
        Assert.Equal("Podaj hasło, aby potwierdzić usunięcie konta.", noPassword.TextOf("[data-testid=delete-error]"));
        Assert.False(web.Identity.AccountDeleted);
        Assert.Null(web.Identity.LastDeleteBody);
    }

    [Fact]
    public async Task A_wrong_password_is_an_alert_the_account_stays_and_the_password_is_not_echoed()
    {
        var (web, browser) = await SignedIn();
        await using var __ = web;
        var page = await browser.PostFormAsync("/konto", Delete("Wrong-Passw0rd-xyz", true));
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal("Nie udało się usunąć konta. Sprawdź hasło i spróbuj ponownie.", page.TextOf("[data-testid=delete-error]"));
        Assert.DoesNotContain("Wrong-Passw0rd-xyz", page.Html);
        Assert.False(web.Identity.AccountDeleted);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/konto")).Status); // still signed in
    }

    [Fact]
    public async Task The_service_being_down_is_said_differently_from_a_wrong_password()
    {
        var (web, browser) = await SignedIn();
        await using var __ = web;
        var inner = web.Identity.Backend.Respond;
        web.Identity.Backend.Respond = r => r.Method == HttpMethod.Delete ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : inner(r);
        var response = await browser.PostFormAsync("/konto", Delete(FakeIdentity.Password, true));
        Assert.Contains("chwilowo niedostępne", response.TextOf("[data-testid=delete-error]"));
    }

    [Fact]
    public async Task Deleting_sends_the_confirmation_word_and_the_password_ends_the_session_and_goes_home()
    {
        var (web, browser) = await SignedIn();
        await using var __ = web;
        var response = await browser.PostFormAsync("/konto", Delete(FakeIdentity.Password, true));
        Assert.Equal(HttpStatusCode.Found, response.Status);
        Assert.Equal("/", response.Location);
        Assert.True(web.Identity.AccountDeleted);
        using var sent = JsonDocument.Parse(web.Identity.LastDeleteBody!);
        Assert.Equal("DELETE", sent.RootElement.GetProperty("confirmation").GetString());
        Assert.Equal(FakeIdentity.Password, sent.RootElement.GetProperty("password").GetString());
        Assert.Contains(response.SetCookies, c => c.StartsWith("gt_session=;")); // the cookie is deleted
        Assert.Equal(HttpStatusCode.Found, (await browser.GetAsync("/konto")).Status);
    }

    [Fact]
    public async Task An_account_without_a_password_deletes_without_asking_for_one()
    {
        var (web, browser) = await SignedIn();
        await using var __ = web;
        web.Identity.HasPassword = false;
        var page = await browser.GetAsync("/konto");
        Assert.Null(page.One("#delete-password"));
        var response = await browser.PostFormAsync("/konto", Delete(null, true));
        Assert.Equal(HttpStatusCode.Found, response.Status);
        using var sent = JsonDocument.Parse(web.Identity.LastDeleteBody!);
        Assert.False(sent.RootElement.TryGetProperty("password", out var __x));
    }

    [Fact]
    public async Task A_forged_post_without_an_antiforgery_token_is_refused()
    {
        var (web, browser) = await SignedIn();
        await using var __ = web;
        var request = new HttpRequestMessage(HttpMethod.Post, "/konto") { Content = new FormUrlEncodedContent(Delete(FakeIdentity.Password, true).Append(new("_handler", "delete-account"))) };
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.SendAsync(request)).Status);
        Assert.False(web.Identity.AccountDeleted);
    }
}

public class AccountClientParseTests
{
    static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void The_account_is_built_field_by_field_and_the_pending_consent_shows_the_required_version()
    {
        var a = AccountClient.Parse(J("{\"email\":\"a@b.c\",\"emailConfirmed\":true,\"accessToken\":\"X\"}"),
            J("{\"terms\":{\"accepted\":false,\"requiredVersion\":\"v2\",\"acceptedVersion\":\"v1\"},\"privacy\":{\"accepted\":true,\"requiredVersion\":\"p2\",\"acceptedVersion\":\"p2\"}}"))!;
        Assert.Equal(new ConsentDto(false, "v2"), a.Terms);
        Assert.Equal(new ConsentDto(true, "p2"), a.Privacy);
        Assert.True(a.EmailConfirmed);
        Assert.True(a.HasPassword); // absent means "has one", as the Next.js reader
        Assert.True(a.ConsentPending);
        Assert.DoesNotContain("X", a.ToString());
    }

    [Theory]
    [InlineData("{}")] [InlineData("{\"email\":5}")] [InlineData("[]")] [InlineData("\"text\"")]
    public void An_answer_without_an_email_is_no_account(string me) => Assert.Null(AccountClient.Parse(J(me), J("{}")));

    [Fact]
    public void An_explicit_false_for_the_password_and_a_missing_consent_block_are_read_strictly()
    {
        var a = AccountClient.Parse(J("{\"email\":\"a@b.c\",\"hasPassword\":false}"), J("{}"))!;
        Assert.False(a.HasPassword);
        Assert.False(a.EmailConfirmed);
        Assert.Equal(new ConsentDto(false, ""), a.Terms);
        Assert.True(a.ConsentPending);
    }
}

public class DeleteAccountFormTests
{
    [Fact]
    public void The_checkbox_is_checked_before_the_password()
    {
        Assert.Equal(Gabinety.Web.Text.Pl.Account.DeleteConfirmRequired, new DeleteAccountForm { Password = "x" }.Problem(true));
        Assert.Equal(Gabinety.Web.Text.Pl.Account.DeleteConfirmRequired, new DeleteAccountForm().Problem(true));
    }

    [Theory]
    [InlineData(null, true)] [InlineData("", true)] [InlineData("  ", true)]
    public void A_password_is_required_when_the_account_has_one(string? password, bool hasPassword) =>
        Assert.Equal(Gabinety.Web.Text.Pl.Account.DeletePasswordRequired, new DeleteAccountForm { Confirm = true, Password = password }.Problem(hasPassword));

    [Fact]
    public void A_complete_form_or_a_passwordless_account_has_no_problem()
    {
        Assert.Null(new DeleteAccountForm { Confirm = true, Password = "x" }.Problem(true));
        Assert.Null(new DeleteAccountForm { Confirm = true }.Problem(false));
    }

    [Fact]
    public void A_refusal_and_an_outage_have_different_texts_and_success_has_none()
    {
        Assert.Equal(Gabinety.Web.Text.Pl.Account.DeleteFailed, DeleteAccountForm.MessageFor(DeleteOutcome.Rejected));
        Assert.Equal(Gabinety.Web.Text.Pl.Account.DeleteUnavailable, DeleteAccountForm.MessageFor(DeleteOutcome.Unavailable));
        Assert.Null(DeleteAccountForm.MessageFor(DeleteOutcome.Deleted));
    }
}
