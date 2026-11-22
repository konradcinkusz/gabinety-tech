using System.Net;
using System.Text;
using System.Text.Json;
using Gabinety.Web.Api;
using Gabinety.Web.Auth;
using Gabinety.Web.Tests.Pages;
using Microsoft.AspNetCore.Http;

namespace Gabinety.Web.Tests.Pipeline;

/// <summary>
/// T-11.07: /zapomniane-haslo and /reset-password. The identity service is a stateful fake with the shapes of authservice v0.3.4 (forgot-password 200 for every address; reset-password 400 with
/// <c>errors</c> as an ARRAY for Identity failures and as an OBJECT keyed by field for model validation; a token is single use).
/// </summary>
public class PasswordPagesTests
{
    const string Token = "TOK-9f8e7d6c5b4a-FIXTURE", Email = "someone.private@example.test", Strong = "Nowe-Haslo-123!";

    /// <summary>A reset service over the identity fake: tokens are issued by <c>Issue</c>, spent by one successful reset.</summary>
    sealed class ResetService
    {
        public HashSet<string> Valid { get; } = [Token];
        public bool OAuthOnly { get; set; }
        public int ForgotStatus { get; set; } = 200;
        public int ResetStatus { get; set; }
        /// <summary>The 400 body of a rejected password, in the shape under test.</summary>
        public string? PasswordRejectionBody { get; set; }
        public string BadTokenBody { get; set; } = "{\"errors\":[\"Invalid token.\"]}";

        public void Install(FakeIdentity identity)
        {
            var inner = identity.Backend.Respond;
            identity.Backend.Respond = r =>
            {
                var path = r.RequestUri!.AbsolutePath;
                if (path == "/api/v1/auth/forgot-password")
                {
                    if (ForgotStatus != 200) return new HttpResponseMessage((HttpStatusCode)ForgotStatus);
                    return FakeBackend.Json(new { message = "If an account with that email exists, a password reset link has been sent.", isOAuthOnly = OAuthOnly });
                }
                if (path == "/api/v1/auth/reset-password")
                {
                    if (ResetStatus != 0) return new HttpResponseMessage((HttpStatusCode)ResetStatus);
                    using var doc = JsonDocument.Parse(r.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                    var token = doc.RootElement.GetProperty("token").GetString()!;
                    if (!Valid.Contains(token)) return FakeBackend.Raw(BadTokenBody, HttpStatusCode.BadRequest);
                    if (PasswordRejectionBody is not null && doc.RootElement.GetProperty("newPassword").GetString() == "Rejected-By-Server-1!") return FakeBackend.Raw(PasswordRejectionBody, HttpStatusCode.BadRequest);
                    Valid.Remove(token);
                    return FakeBackend.Json(new { message = "Password has been reset successfully. You can now sign in with your new password." });
                }
                return inner(r);
            };
        }
    }

    static (WebFactory Web, ResetService Service, LogCapture Logs) Host()
    {
        var logs = new LogCapture();
        var web = new WebFactory().LogTo(logs);
        var service = new ResetService();
        service.Install(web.Identity);
        return (web, service, logs);
    }

    /// <summary>The document with the values that differ per request blanked out: the antiforgery token of every form and the encrypted component-state comment.</summary>
    static string Masked(string html) => Page.Squash(System.Text.RegularExpressions.Regex.Replace(System.Text.RegularExpressions.Regex.Replace(html, "(__RequestVerificationToken[^>]*?value=\")[^\"]*", "$1"), "<!--Blazor-Server-Component-State:[^>]*-->", "<!--state-->"));

    static string ResetUrl(string token = Token, string email = Email) => $"/reset-password?token={Uri.EscapeDataString(token)}&email={Uri.EscapeDataString(email)}";

    static Dictionary<string, string> ResetForm(string password = Strong, string? confirmation = null) =>
        new() { ["Model.Password"] = password, ["Model.Confirmation"] = confirmation ?? password };

    // ---- /zapomniane-haslo ----

    [Fact]
    public async Task The_sign_in_page_links_to_the_forgot_page()
    {
        await using var web = new WebFactory();
        var page = await web.NewBrowser().GetAsync("/logowanie");
        var link = page.All("a").Single(a => a.TextContent == "Nie pamiętasz hasła?");
        Assert.Equal("/zapomniane-haslo", link.GetAttribute("href"));
    }

    [Fact]
    public async Task The_forgot_page_is_a_static_form_with_antiforgery_one_label_and_is_noindex()
    {
        var (web, _, _) = Host();
        await using var _web = web;
        var page = await web.NewBrowser().GetAsync("/zapomniane-haslo");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal("Nie pamiętasz hasła?", page.TextOf("h1"));
        var form = page.One("form[method=post]")!;
        Assert.NotNull(form.QuerySelector("input[name=__RequestVerificationToken]"));
        Assert.Equal("forgot-password", form.QuerySelector("input[name=_handler]")!.GetAttribute("value"));
        Assert.Equal("Adres e-mail", form.QuerySelector("label[for=forgot-email]")!.TextContent);
        Assert.Equal("email", form.QuerySelector("#forgot-email")!.GetAttribute("type"));
        Assert.StartsWith("noindex", page.One("meta[name=robots]")!.GetAttribute("content"));
    }

    [Fact]
    public async Task A_known_and_an_unknown_address_get_the_identical_answer_same_status_same_text_same_markup()
    {
        var (web, _, _) = Host();
        await using var _web = web;
        var known = await web.NewBrowser().PostFormAsync("/zapomniane-haslo", new Dictionary<string, string> { ["Model.Email"] = "demo-client@demo.example" });
        var unknown = await web.NewBrowser().PostFormAsync("/zapomniane-haslo", new Dictionary<string, string> { ["Model.Email"] = "nobody-at-all@demo.example" });
        Assert.Equal(HttpStatusCode.OK, known.Status);
        Assert.Equal(known.Status, unknown.Status);
        Assert.Equal("Jeśli konto o tym adresie istnieje, wysłaliśmy wiadomość z linkiem.", known.TextOf("[data-testid=forgot-answer]"));
        Assert.Equal(known.TextOf("[data-testid=forgot-answer]"), unknown.TextOf("[data-testid=forgot-answer]"));
        Assert.Equal(Masked(known.Html), Masked(unknown.Html)); // the whole document: the page does not even echo the address
        Assert.DoesNotContain("demo-client@demo.example", known.Html);
        Assert.DoesNotContain("nobody-at-all@demo.example", unknown.Html);
        Assert.Null(known.One("[data-testid=forgot-oauth]"));
    }

    [Fact]
    public async Task An_account_that_signs_in_with_GitHub_gets_one_more_sentence()
    {
        var (web, service, _) = Host();
        await using var _web = web;
        service.OAuthOnly = true;
        var page = await web.NewBrowser().PostFormAsync("/zapomniane-haslo", new Dictionary<string, string> { ["Model.Email"] = "github-person@demo.example" });
        Assert.Equal("Jeśli konto o tym adresie istnieje, wysłaliśmy wiadomość z linkiem.", page.TextOf("[data-testid=forgot-answer]"));
        Assert.Equal("Jeśli to konto korzysta z logowania przez GitHub, nie ma hasła: zaloguj się przez GitHub.", page.TextOf("[data-testid=forgot-oauth]"));
    }

    [Fact]
    public async Task A_malformed_or_empty_address_is_refused_on_the_page_without_asking_the_service()
    {
        var (web, _, _) = Host();
        await using var _web = web;
        foreach (var (typed, message) in new[] { ("", "Podaj adres e-mail."), ("not-an-address", "Podaj poprawny adres e-mail.") })
        {
            var page = await web.NewBrowser().PostFormAsync("/zapomniane-haslo", new Dictionary<string, string> { ["Model.Email"] = typed });
            Assert.Equal(HttpStatusCode.OK, page.Status);
            Assert.Equal("Popraw 1 pole", page.One("[data-testid=form-summary] h2")!.TextContent);
            Assert.Equal(message, page.One(".field__error")!.TextContent.Replace("Błąd: ", "").Trim());
        }
        Assert.DoesNotContain(web.Identity.Backend.Requests, x => x.PathAndQuery.Contains("forgot-password"));
    }

    [Fact]
    public async Task Rate_limited_shows_the_existing_wait_text_and_an_outage_its_own_message_both_keep_the_form()
    {
        var (web, service, _) = Host();
        await using var _web = web;
        service.ForgotStatus = 429;
        var limited = await web.NewBrowser().PostFormAsync("/zapomniane-haslo", new Dictionary<string, string> { ["Model.Email"] = "a@demo.example" });
        Assert.Equal("Zbyt wiele prób. Spróbuj ponownie za minutę.", limited.TextOf("[data-testid=forgot-error]"));
        Assert.NotNull(limited.One("form[method=post]"));
        Assert.Equal("a@demo.example", limited.One("#forgot-email")!.GetAttribute("value")); // the address is kept so the person can try again
        service.ForgotStatus = 503;
        var down = await web.NewBrowser().PostFormAsync("/zapomniane-haslo", new Dictionary<string, string> { ["Model.Email"] = "a@demo.example" });
        Assert.Equal("Nie udało się teraz wysłać wiadomości. Spróbuj ponownie za chwilę.", down.TextOf("[data-testid=forgot-error]"));
        Assert.Null(down.One("[data-testid=forgot-answer]"));
    }

    // ---- /reset-password ----

    [Fact]
    public async Task The_reset_page_keeps_the_token_and_address_in_hidden_fields_only_and_is_noindex_with_no_referrer()
    {
        var (web, _, _) = Host();
        await using var _web = web;
        var page = await web.NewBrowser().GetAsync(ResetUrl());
        Assert.Equal(HttpStatusCode.OK, page.Status);
        var form = page.One("form[method=post]")!;
        Assert.Equal("/reset-password", form.GetAttribute("action")); // the post goes to the bare path: no token in its URL
        Assert.Equal(Token, form.QuerySelector("input[type=hidden][name='Model.Token']")!.GetAttribute("value"));
        Assert.Equal(Email, form.QuerySelector("input[type=hidden][name='Model.Email']")!.GetAttribute("value"));
        Assert.DoesNotContain(Token, page.Text);
        Assert.DoesNotContain(Email, page.Text);
        Assert.All(page.All("[href], [src], [action]"), e => Assert.DoesNotContain(Token, string.Join(" ", e.Attributes.Select(a => a.Value))));
        Assert.Equal(1, page.Html.Split(Token).Length - 1); // exactly one occurrence: the hidden field
        Assert.Equal("noindex,nofollow", page.One("meta[name=robots]")!.GetAttribute("content"));
        Assert.Equal("no-referrer", page.One("meta[name=referrer]")!.GetAttribute("content"));
        Assert.Equal(["co najmniej 8 znaków", "mała i wielka litera", "cyfra", "znak specjalny"], page.All("#reset-password-rules li").Select(li => li.TextContent));
        Assert.Equal("password", form.QuerySelector("#reset-password")!.GetAttribute("type"));
        Assert.Equal("password", form.QuerySelector("#reset-confirmation")!.GetAttribute("type"));
        var reveals = form.QuerySelectorAll("button[data-enhance=reveal]");
        Assert.Equal(2, reveals.Length);
        Assert.All(reveals, b => Assert.True(b.HasAttribute("hidden")));
        Assert.NotNull(form.QuerySelector("input[name=__RequestVerificationToken]"));
    }

    [Fact]
    public async Task A_valid_reset_signs_nobody_in_and_ends_at_the_sign_in_page_with_the_flash()
    {
        var (web, service, _) = Host();
        await using var _web = web;
        var browser = web.NewBrowser();
        var done = await browser.PostFormAsync(ResetUrl(), ResetForm());
        Assert.Equal(HttpStatusCode.Redirect, done.Status);
        Assert.Equal("/logowanie?flash=password-reset", done.Location);
        Assert.DoesNotContain(Token, done.Location);
        Assert.DoesNotContain("gt_session", browser.Cookies.Keys);
        Assert.DoesNotContain(browser.SetCookieLog, c => c.StartsWith("gt_session", StringComparison.Ordinal));
        Assert.DoesNotContain(web.Identity.Backend.Requests, x => x.PathAndQuery.Contains("/auth/login") || x.PathAndQuery.Contains("/auth/refresh"));
        Assert.DoesNotContain(Token, service.Valid); // spent
        var login = await browser.GetAsync(done.Location!);
        Assert.Equal("Hasło zostało zmienione. Zaloguj się.", login.TextOf("[data-testid=flash] span"));
        var account = await browser.GetAsync("/konto");
        Assert.Equal(HttpStatusCode.Redirect, account.Status); // still a visitor
        Assert.StartsWith("/logowanie", account.Location);
    }

    [Fact]
    public async Task The_body_sent_to_the_service_is_the_address_the_token_and_the_password_in_json_and_nowhere_in_a_url()
    {
        var (web, _, _) = Host();
        await using var _web = web;
        await web.NewBrowser().PostFormAsync(ResetUrl(), ResetForm());
        var call = web.Identity.Backend.Requests.Single(x => x.PathAndQuery.Contains("reset-password"));
        Assert.Equal("/api/v1/auth/reset-password", call.PathAndQuery);
        using var body = JsonDocument.Parse(call.Body!);
        Assert.Equal(Token, body.RootElement.GetProperty("token").GetString());
        Assert.Equal(Email, body.RootElement.GetProperty("email").GetString());
        Assert.Equal(Strong, body.RootElement.GetProperty("newPassword").GetString());
    }

    [Fact]
    public async Task A_reused_token_shows_the_one_bad_token_message_and_a_link_to_ask_for_a_new_one()
    {
        var (web, _, _) = Host();
        await using var _web = web;
        Assert.Equal(HttpStatusCode.Redirect, (await web.NewBrowser().PostFormAsync(ResetUrl(), ResetForm())).Status);
        var again = await web.NewBrowser().PostFormAsync(ResetUrl(), ResetForm("Another-Passw0rd!"));
        Assert.Equal(HttpStatusCode.OK, again.Status);
        Assert.Equal("Link do zmiany hasła jest nieprawidłowy, został już użyty albo wygasł.", again.TextOf("[data-testid=reset-bad-token]"));
        var link = again.One(".card a.btn")!;
        Assert.Equal("Poproś o nowy link", link.TextContent);
        Assert.Equal("/zapomniane-haslo", link.GetAttribute("href"));
        Assert.Null(again.One("form[method=post]"));
        Assert.DoesNotContain(Token, again.Html);
        Assert.DoesNotContain(Email, again.Html);
    }

    [Theory]
    [InlineData("{\"errors\":[\"Invalid token.\"]}")]                                    // Identity's own failure: an array of strings
    [InlineData("{\"title\":\"One or more validation errors occurred.\",\"status\":400,\"errors\":{\"Token\":[\"Reset token is required\"]}}")] // [ApiController] model validation: an object keyed by field
    [InlineData("{\"errors\":{\"Email\":[\"Invalid email format\"]}}")]
    [InlineData("not json at all")]
    public async Task A_bad_token_is_one_message_whatever_shape_the_service_answers_in(string body)
    {
        var (web, service, _) = Host();
        await using var _web = web;
        service.BadTokenBody = body;
        var page = await web.NewBrowser().PostFormAsync(ResetUrl("some-other-token"), ResetForm());
        Assert.Equal("Link do zmiany hasła jest nieprawidłowy, został już użyty albo wygasł.", page.TextOf("[data-testid=reset-bad-token]"));
    }

    [Theory]
    [InlineData("{\"errors\":[\"Passwords must have at least one digit ('0'-'9').\",\"Passwords must have at least one uppercase ('A'-'Z').\"]}")]
    [InlineData("{\"title\":\"One or more validation errors occurred.\",\"status\":400,\"errors\":{\"NewPassword\":[\"Password must be between 8 and 100 characters\"]}}")]
    public async Task A_password_the_service_refuses_keeps_the_token_usable_and_shows_one_error_under_the_field_in_both_shapes(string body)
    {
        var (web, service, _) = Host();
        await using var _web = web;
        service.PasswordRejectionBody = body;
        var page = await web.NewBrowser().PostFormAsync(ResetUrl(), ResetForm("Rejected-By-Server-1!"));
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal("Popraw 1 pole", page.One("[data-testid=form-summary] h2")!.TextContent);
        Assert.Equal("Hasło nie spełnia wymagań. Wybierz inne hasło.", page.One("#reset-password-error")!.TextContent.Replace("Błąd: ", "").Trim());
        Assert.Null(page.One("[data-testid=reset-bad-token]"));
        Assert.Equal(Token, page.One("input[name='Model.Token']")!.GetAttribute("value")); // the form is still there with the token
        Assert.DoesNotContain("Rejected-By-Server-1!", page.Html);
        Assert.DoesNotContain("Passwords must", page.Html); // the service's English is never shown
        Assert.Contains(Token, service.Valid);
        var retry = await web.NewBrowser().PostFormAsync(ResetUrl(), ResetForm());
        Assert.Equal(HttpStatusCode.Redirect, retry.Status);
    }

    [Fact]
    public async Task Every_problem_is_shown_at_once_and_the_service_is_not_asked()
    {
        var (web, _, _) = Host();
        await using var _web = web;
        var page = await web.NewBrowser().PostFormAsync(ResetUrl(), ResetForm("abc", "abd"));
        var summary = page.One("[data-testid=form-summary]")!;
        Assert.Equal("Popraw 2 pola", summary.QuerySelector("h2")!.TextContent);
        Assert.Equal(["#reset-password", "#reset-confirmation"], summary.QuerySelectorAll("a").Select(a => a.GetAttribute("href")));
        Assert.Equal("true", page.One("#reset-password")!.GetAttribute("aria-invalid"));
        Assert.Equal("true", page.One("#reset-confirmation")!.GetAttribute("aria-invalid"));
        Assert.Contains("co najmniej 8 znaków", page.One("#reset-password-error")!.TextContent);
        Assert.Contains("Hasła nie są takie same.", page.One("#reset-confirmation-error")!.TextContent);
        Assert.DoesNotContain(web.Identity.Backend.Requests, x => x.PathAndQuery.Contains("reset-password"));
        Assert.Equal(Token, page.One("input[name='Model.Token']")!.GetAttribute("value"));
        Assert.DoesNotContain("abc", page.Html.Replace("abcdef", "")); // the typed password is not echoed
    }

    [Theory]
    [InlineData("/reset-password")]
    [InlineData("/reset-password?token=only-a-token")]
    [InlineData("/reset-password?email=only%40example.test")]
    public async Task A_link_without_both_values_is_the_bad_token_state_with_no_form(string url)
    {
        var (web, _, _) = Host();
        await using var _web = web;
        var page = await web.NewBrowser().GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.NotNull(page.One("[data-testid=reset-bad-token]"));
        Assert.Null(page.One("form[method=post]"));
        Assert.Equal("/zapomniane-haslo", page.One(".card a.btn")!.GetAttribute("href"));
        Assert.Equal("noindex,nofollow", page.One("meta[name=robots]")!.GetAttribute("content"));
    }

    [Fact]
    public async Task Rate_limited_and_an_outage_keep_the_form_and_show_their_own_text()
    {
        var (web, service, _) = Host();
        await using var _web = web;
        service.ResetStatus = 429;
        var limited = await web.NewBrowser().PostFormAsync(ResetUrl(), ResetForm());
        Assert.Equal("Zbyt wiele prób. Spróbuj ponownie za minutę.", limited.TextOf("[data-testid=reset-error]"));
        Assert.Equal(Token, limited.One("input[name='Model.Token']")!.GetAttribute("value"));
        service.ResetStatus = 503;
        var down = await web.NewBrowser().PostFormAsync(ResetUrl(), ResetForm());
        Assert.Equal("Nie udało się teraz zmienić hasła. Spróbuj ponownie za chwilę.", down.TextOf("[data-testid=reset-error]"));
        Assert.Contains(Token, service.Valid);
    }

    // ---- the log ----

    [Fact]
    public async Task No_log_line_of_a_whole_reset_flow_contains_the_token_or_the_address_or_a_password()
    {
        var (web, service, logs) = Host();
        await using var _web = web;
        var browser = web.NewBrowser();
        await browser.PostFormAsync("/zapomniane-haslo", new Dictionary<string, string> { ["Model.Email"] = Email });
        await browser.GetAsync(ResetUrl());
        await browser.PostFormAsync(ResetUrl(), ResetForm("abc", "abd"));                  // problems
        await browser.PostFormAsync(ResetUrl(), ResetForm());                              // success
        await browser.PostFormAsync(ResetUrl(), ResetForm());                              // reuse: the bad-token state
        service.ResetStatus = 503;
        await browser.PostFormAsync(ResetUrl("TOK-other-FIXTURE-2"), ResetForm());          // an outage while a token is in flight
        Assert.NotEmpty(logs.Lines);
        foreach (var needle in new[] { Token, "TOK-other-FIXTURE-2", Email, Uri.EscapeDataString(Email), Uri.EscapeDataString(Token), Strong, "abc" })
            Assert.DoesNotContain(logs.Lines, l => l.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    // ---- the client, both 400 shapes, without a page ----

    static IdentityClient Client(string body, int status = 400) =>
        new(new HttpClient(new FakeBackend { Respond = _ => FakeBackend.Raw(body, (HttpStatusCode)status) }) { BaseAddress = new Uri("http://auth.test") }, new VisitorAddressAccessor(new HttpContextAccessor()));

    [Theory]
    [InlineData("{\"errors\":[\"Invalid token.\"]}", ResetOutcome.InvalidToken)]
    [InlineData("{\"errors\":[\"Passwords must be at least 8 characters.\"]}", ResetOutcome.PasswordRejected)]
    [InlineData("{\"errors\":[\"Passwords must have at least one digit ('0'-'9').\",\"Passwords must have at least one lowercase ('a'-'z').\"]}", ResetOutcome.PasswordRejected)]
    [InlineData("{\"errors\":[\"Invalid token.\",\"Passwords must have at least one digit ('0'-'9').\"]}", ResetOutcome.InvalidToken)]
    [InlineData("{\"errors\":{\"NewPassword\":[\"Password must be between 8 and 100 characters\"]}}", ResetOutcome.PasswordRejected)]
    [InlineData("{\"errors\":{\"Token\":[\"Reset token is required\"]}}", ResetOutcome.InvalidToken)]
    [InlineData("{\"errors\":{\"Email\":[\"Invalid email format\"],\"NewPassword\":[\"New password is required\"]}}", ResetOutcome.InvalidToken)]
    [InlineData("{\"type\":\"https://tools.ietf.org/html/rfc9110#section-15.5.1\",\"title\":\"One or more validation errors occurred.\",\"status\":400,\"errors\":{\"NewPassword\":[\"x\"]}}", ResetOutcome.PasswordRejected)]
    [InlineData("{\"error\":\"something\"}", ResetOutcome.InvalidToken)]
    [InlineData("[]", ResetOutcome.InvalidToken)]
    [InlineData("<html>", ResetOutcome.InvalidToken)]
    public async Task The_client_reads_both_400_shapes_of_reset_password(string body, ResetOutcome expected) =>
        Assert.Equal(expected, await Client(body).ResetPasswordAsync(Email, Token, Strong));

    [Theory]
    [InlineData(200, ResetOutcome.Done)]
    [InlineData(429, ResetOutcome.RateLimited)]
    [InlineData(500, ResetOutcome.Unavailable)]
    [InlineData(404, ResetOutcome.Unavailable)]
    public async Task The_client_maps_the_other_statuses_of_reset_password(int status, ResetOutcome expected) =>
        Assert.Equal(expected, await Client("{\"message\":\"ok\"}", status).ResetPasswordAsync(Email, Token, Strong));

    [Theory]
    [InlineData(200, "{\"message\":\"m\",\"isOAuthOnly\":false}", ForgotOutcome.Sent, false)]
    [InlineData(200, "{\"message\":\"m\",\"isOAuthOnly\":true}", ForgotOutcome.Sent, true)]
    [InlineData(200, "garbage", ForgotOutcome.Sent, false)]
    [InlineData(400, "{\"errors\":{\"Email\":[\"Invalid email format\"]}}", ForgotOutcome.InvalidEmail, false)]
    [InlineData(429, "{}", ForgotOutcome.RateLimited, false)]
    [InlineData(502, "{}", ForgotOutcome.Unavailable, false)]
    public async Task The_client_reads_the_answer_of_forgot_password(int status, string body, ForgotOutcome outcome, bool oauthOnly) =>
        Assert.Equal(new ForgotResult(outcome, oauthOnly), await Client(body, status).ForgotPasswordAsync(Email));

    [Fact]
    public async Task A_network_failure_is_unavailable_and_the_exception_is_not_kept()
    {
        var client = new IdentityClient(new HttpClient(new FakeBackend { Respond = FakeBackend.Throw(new HttpRequestException(Token + Email)) }) { BaseAddress = new Uri("http://auth.test") }, new VisitorAddressAccessor(new HttpContextAccessor()));
        Assert.Equal(ResetOutcome.Unavailable, await client.ResetPasswordAsync(Email, Token, Strong));
        Assert.Equal(ForgotOutcome.Unavailable, (await client.ForgotPasswordAsync(Email)).Outcome);
    }

    // ---- the model ----

    [Theory]
    [InlineData("Nowe-Haslo-123!", "Nowe-Haslo-123!", 0)]
    [InlineData("", "", 2)]
    [InlineData("short1A!", "short1A?", 1)]
    [InlineData("alllowercase1!", "alllowercase1!", 1)]
    [InlineData("Nowe-Haslo-123!", "", 1)]
    public void The_model_counts_every_problem(string password, string confirmation, int problems)
    {
        var model = new Gabinety.Web.Components.Pages.Account.ResetPasswordModel { Token = "t", Email = "e@e.pl", Password = password, Confirmation = confirmation };
        Assert.Equal(problems, model.Validate().Count);
        Assert.False(model.LinkIsIncomplete);
        Assert.True(new Gabinety.Web.Components.Pages.Account.ResetPasswordModel { Token = "t" }.LinkIsIncomplete);
    }
}
