extern alias WebApp;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gabinety.Integration.Tests.RealAuth;
using Xunit.Abstractions;
using Pl = WebApp::Gabinety.Web.Text.Pl;
using PlAdmin = WebApp::Gabinety.Web.Text.PlAdmin;

namespace Gabinety.Integration.Tests.BlazorAuth;

/// <summary>
/// The Blazor host's authentication (<c>src/Gabinety.Web/Auth</c>, the OAuth and account endpoints, the login/registration/account pages) against the REAL authservice image, driven through the host's
/// own HTTP surface, in process. This is what the Vitest file <c>real-authservice.test.ts</c> of the retired Next.js app proved for its handlers; the Blazor host was only tested with fakes and
/// against the DevIdentity stand-in until now. NOT covered (needs things a test run does not have): the GitHub callback leg with a real OAuth app, and the delivery of the confirmation e-mail.
/// </summary>
[Trait("tier", "integration")]
[Collection(RealAuthCollection.Name)]
public class BlazorRealAuthTests(RealAuthFixture auth, ITestOutputHelper output)
{
    const string Password = RealAuthApi.UserPassword;

    static Task<WebPage> Register(WebBrowser visitor, string email, string password) =>
        visitor.Submit("/rejestracja", "register", ("Model.Email", email), ("Model.Password", password), ("Model.Consent", "true"));

    // ---- registration ------------------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Registration_through_the_form_signs_in_when_authservice_answers_200()
    {
        using var host = new BlazorHost(auth.Main);
        var visitor = host.Visitor();
        var email = RealAuthApi.NewEmail();

        var page = await Register(visitor, email, Password);

        Assert.Equal(HttpStatusCode.Found, page.Status);
        Assert.Equal("/", page.Location);
        Assert.NotNull(visitor.Session);
        var account = await visitor.Get("/konto");
        Assert.Equal(HttpStatusCode.OK, account.Status);
        Assert.Contains(email, account.Text);
        Assert.Contains(Pl.Account.TermsAccepted, account.Text); // the consent versions were fetched from the real service, sent back verbatim and recorded
        Assert.Contains(Pl.Account.PrivacyAccepted, account.Text);
    }

    [Fact]
    public async Task Registration_where_confirmation_is_required_is_the_202_state_with_no_session()
    {
        using var host = new BlazorHost(auth.Confirm);
        var visitor = host.Visitor();
        var email = RealAuthApi.NewEmail();

        var page = await Register(visitor, email, Password);

        Assert.Equal(HttpStatusCode.Found, page.Status);
        Assert.Equal("/rejestracja/sprawdz-skrzynke", page.Location);
        Assert.Null(visitor.Session);
        Assert.Null(page.SetCookie("gt_session"));
        Assert.Equal(HttpStatusCode.OK, (await visitor.Get(page.Location!)).Status);
        // authservice refuses the unconfirmed account with the generic 401 (docs/operations/AUTH.md section 9, finding 3): the web says "wrong e-mail or password", and issues nothing.
        var login = await visitor.SignIn(email, Password);
        Assert.Equal(HttpStatusCode.OK, login.Status);
        Assert.Equal(Pl.Login.InvalidCredentials, login.Alert);
        Assert.Null(visitor.Session);
    }

    [Fact]
    public async Task An_address_that_is_taken_is_the_one_uniform_failure_and_signs_nobody_in()
    {
        using var host = new BlazorHost(auth.Main);
        var taken = (await auth.Main.Register()).Email;
        var visitor = host.Visitor();

        var page = await Register(visitor, taken, Password);

        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal(Pl.Register.Failed, page.Alert);
        Assert.Null(visitor.Session);
        Assert.DoesNotContain(Password, page.Html); // the password is never echoed back
    }

    // ---- login: success, wrong password, lockout, the 429 wait message ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Login_succeeds_goes_back_to_the_page_asked_for_and_a_wrong_password_and_an_unknown_address_read_the_same()
    {
        using var host = new BlazorHost(auth.Main);
        var user = await auth.Main.Register();

        var ok = host.Visitor();
        var signedIn = await ok.SignIn(user.Email, Password, "/konto");
        Assert.Equal(HttpStatusCode.Found, signedIn.Status);
        Assert.Equal("/konto", signedIn.Location);
        Assert.NotNull(ok.Session);

        var wrong = host.Visitor();
        var refused = await wrong.SignIn(user.Email, Password + "-wrong");
        var unknown = await host.Visitor().SignIn(RealAuthApi.NewEmail(), Password);
        Assert.Equal(HttpStatusCode.OK, refused.Status);
        Assert.Equal(Pl.Login.InvalidCredentials, refused.Alert);
        Assert.Equal(refused.Alert, unknown.Alert);
        Assert.Null(wrong.Session);
        Assert.Null(refused.SetCookie("gt_session"));
        Assert.DoesNotContain(Password, refused.Html);
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_and_only_the_right_password_is_told_so()
    {
        using var host = new BlazorHost(auth.Main);
        var user = await auth.Main.Register();
        var visitor = host.Visitor();
        for (var i = 0; i < 5; i++) Assert.Equal(Pl.Login.InvalidCredentials, (await visitor.SignIn(user.Email, Password + "-wrong")).Alert);

        var rightButLocked = await visitor.SignIn(user.Email, Password);
        var wrongWhileLocked = await visitor.SignIn(user.Email, Password + "-wrong");

        Assert.Equal(Pl.Login.LockedOut, rightButLocked.Alert);
        Assert.Equal(Pl.Login.InvalidCredentials, wrongWhileLocked.Alert); // a guesser learns nothing
        Assert.Null(visitor.Session);
    }

    [Fact]
    public async Task A_visitor_over_the_limit_is_told_to_wait_and_another_visitor_is_served_because_the_web_forwards_each_address()
    {
        using var host = new BlazorHost(auth.Main);
        const string noisy = "10.201.0.1", other = "10.201.0.2";
        var user = await auth.Main.Register();

        // 30 sign-ins at once from one address: 20 pass (as wrong passwords), 5 queue at authservice (longer than the host waits: "unavailable"), the rest are 429.
        var burst = await Task.WhenAll(Enumerable.Range(0, 30).Select(_ => host.Visitor(noisy).SignIn(user.Email, Password + "-wrong")));
        var messages = burst.Select(p => p.Alert).ToList();
        output.WriteLine(string.Join(" | ", messages.GroupBy(m => m).Select(g => $"{g.Count()}x {g.Key}")));
        Assert.Contains(Pl.Login.RateLimited, messages);
        Assert.Contains(Pl.Login.InvalidCredentials, messages);
        Assert.All(messages, m => Assert.Contains(m, new[] { Pl.Login.RateLimited, Pl.Login.InvalidCredentials, Pl.Login.Unavailable }));

        var served = await host.Visitor(other).SignIn(RealAuthApi.NewEmail(), Password);
        Assert.Equal(Pl.Login.InvalidCredentials, served.Alert); // not "slow down": the other visitor has a bucket of its own
        var again = await host.Visitor(noisy).SignIn(user.Email, Password);
        Assert.Contains(again.Alert, new[] { Pl.Login.RateLimited, Pl.Login.Unavailable }); // the first visitor is still limited, and even the right password waits
    }

    [Fact]
    public async Task Two_factor_sign_in_asks_for_the_code_on_its_own_page_and_a_wrong_code_keeps_the_challenge()
    {
        using var host = new BlazorHost(auth.Main);
        var user = await auth.Main.Register();
        using var api = auth.Main.Client();
        var setup = await (await api.Send(HttpMethod.Post, "api/v1/auth/2fa/enable", user.AccessToken)).Json();
        var key = setup.GetProperty("sharedKey").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await api.Send(HttpMethod.Post, "api/v1/auth/2fa/verify", user.AccessToken, new { code = RealAuthApi.Totp(key, DateTimeOffset.UtcNow) })).StatusCode);
        var visitor = host.Visitor();

        var first = await visitor.SignIn(user.Email, Password, "/konto");

        Assert.Equal(HttpStatusCode.Found, first.Status);
        Assert.Equal("/logowanie/kod?redirect=%2Fkonto", first.Location);
        Assert.Null(visitor.Session);
        Assert.NotNull(first.SetCookie("gt_2fa"));

        var wrong = await visitor.Submit("/logowanie/kod?redirect=%2Fkonto", "two-factor", ("Model.Code", "000000"));
        Assert.Equal(HttpStatusCode.OK, wrong.Status);
        Assert.Equal(Pl.TwoFactor.Invalid, wrong.Alert);
        Assert.Null(visitor.Session);

        var done = await visitor.Submit("/logowanie/kod?redirect=%2Fkonto", "two-factor", ("Model.Code", RealAuthApi.Totp(key, DateTimeOffset.UtcNow)));
        Assert.Equal(HttpStatusCode.Found, done.Status);
        Assert.Equal("/konto", done.Location);
        Assert.NotNull(visitor.Session);
        Assert.Equal(HttpStatusCode.OK, (await visitor.Get("/konto")).Status);
    }

    // ---- the cookie ------------------------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_session_cookie_is_HttpOnly_Lax_host_only_holds_no_token_and_stays_well_under_the_size_budget_for_every_role_shape()
    {
        using var host = new BlazorHost(auth.Main);
        var owner = await auth.Main.LoginOwner();               // SuperAdmin: the role is ONE string in the token
        var two = await auth.Main.UserWithRoles("Admin", "User"); // two roles: an array
        var plain = await auth.Main.Register();

        foreach (var (email, password) in new[] { (owner.Email, owner.Password), (two.Email, two.Password), (plain.Email, Password) })
        {
            var visitor = host.Visitor();
            var page = await visitor.SignIn(email, password);
            var cookie = page.SetCookie("gt_session")!;
            Assert.NotNull(cookie);
            Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("domain=", cookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("expires=", cookie, StringComparison.OrdinalIgnoreCase); // persistent, seven days
            Assert.DoesNotContain("secure", cookie, StringComparison.OrdinalIgnoreCase); // the site is http here
            Assert.True(cookie.Length < 3500, $"the session cookie is {cookie.Length} bytes");
            Assert.Single(page.SetCookies, c => c.StartsWith("gt_session"));
            Assert.DoesNotContain(page.SetCookies, c => c.StartsWith("gt_at") || c.StartsWith("gt_rt"));
            var value = visitor.Session!;
            Assert.DoesNotContain("eyJ", value);          // no JWT anywhere in what the browser holds
            Assert.DoesNotContain(host.RefreshTokenIn(value), cookie); // the refresh token is sealed, not visible
            output.WriteLine($"{email.Split('@')[0]}: {cookie.Length} bytes, roles {string.Join(",", host.RolesIn(value))}");
        }
    }

    [Fact]
    public async Task The_cookie_is_Secure_when_the_site_is_served_over_https()
    {
        using var host = new BlazorHost(auth.Main, site: "https://gabinety.example");
        var user = await auth.Main.Register();

        var page = await host.Visitor().SignIn(user.Email, Password);

        Assert.Equal(HttpStatusCode.Found, page.Status);
        Assert.Contains("secure", page.SetCookie("gt_session")!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_tampered_cookie_is_signed_out_and_so_is_one_cut_short_or_from_another_key_ring()
    {
        using var host = new BlazorHost(auth.Main);
        var user = await auth.Main.Register();
        var cookie = (await host.Visitor().SignedIn(user.Email, Password)).Session!;
        var flipped = cookie[..20] + (cookie[20] == 'A' ? 'B' : 'A') + cookie[21..];
        string[] bad = [flipped, cookie[..^8], cookie + "x", "garbage"];

        foreach (var value in bad)
        {
            var page = await host.Visitor().WithCookie("gt_session", value).Get("/konto");
            Assert.Equal(HttpStatusCode.Found, page.Status);
            Assert.StartsWith("/logowanie?redirect=%2Fkonto", page.Location);
        }

        using var elsewhere = new BlazorHost(auth.Main); // its own key ring: it cannot read this host's cookie
        Assert.StartsWith("/logowanie", (await elsewhere.Visitor().WithCookie("gt_session", cookie).Get("/konto")).Location);
    }

    // ---- refresh: rotation, replay, concurrency, a deploy -----------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_access_token_about_to_expire_is_renewed_from_the_cookie_and_the_rotated_refresh_token_reaches_the_cookie()
    {
        using var host = new BlazorHost(auth.Main);
        var user = await auth.Main.Register();
        var visitor = await host.Visitor().SignedIn(user.Email, Password);
        var before = host.RefreshTokenIn(visitor.Session!);

        host.Clock.Offset = TimeSpan.FromMinutes(59) + TimeSpan.FromSeconds(40); // the one-hour token has 20 s left: the host renews it before using it
        var page = await visitor.Get("/konto");
        host.Clock.Offset = TimeSpan.Zero;

        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Contains(user.Email, page.Text);
        Assert.NotNull(page.SetCookie("gt_session")); // the response that could, wrote the rotated token
        var after = host.RefreshTokenIn(visitor.Session!);
        Assert.NotEqual(before, after);
        using var api = auth.Main.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.PostAsJsonAsync("api/v1/auth/refresh", new { refreshToken = before })).StatusCode); // the old one is spent (this also revokes the family: see the next test)
    }

    [Fact]
    public async Task Concurrent_requests_of_a_session_after_a_deploy_spend_the_refresh_token_once()
    {
        var user = await auth.Main.Register();
        using var first = new BlazorHost(auth.Main);
        var cookie = (await first.Visitor().SignedIn(user.Email, Password)).Session!;
        var original = first.RefreshTokenIn(cookie);

        // A new process (an empty session cache, the same key ring): the cookie's refresh token is all it has, and 8 requests arrive together. A second spend of the single-use token would make
        // authservice revoke the whole family and the session would end for everybody, so every one of these must be answered, and answered as the signed-in user.
        using var second = new BlazorHost(auth.Main, first.KeysDirectory);
        var pages = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => second.Visitor().WithCookie("gt_session", cookie).Get("/konto")));

        Assert.All(pages, p => { Assert.Equal(HttpStatusCode.OK, p.Status); Assert.Contains(user.Email, p.Text); });
        var rotated = pages.Select(p => p.SetCookie("gt_session")).Where(c => c is not null).Select(c => c!.Split(';')[0]["gt_session=".Length..]).ToList();
        var tokens = rotated.Select(second.RefreshTokenIn).Distinct().ToList(); // (the sealed values differ: each seal has its own nonce)
        Assert.Single(tokens); // every response carries the same newest refresh token
        var newest = tokens[0];
        Assert.NotEqual(original, newest);

        // The session is alive: the newest cookie works in a third process.
        using var third = new BlazorHost(auth.Main, first.KeysDirectory);
        var next = await third.Visitor().WithCookie("gt_session", rotated[0]).Get("/konto");
        Assert.Equal(HttpStatusCode.OK, next.Status);
        Assert.Contains(user.Email, next.Text);
    }

    [Fact]
    public async Task A_replayed_refresh_token_ends_the_session_the_cookie_is_deleted_and_the_family_is_gone()
    {
        var user = await auth.Main.Register();
        using var first = new BlazorHost(auth.Main);
        var oldCookie = (await first.Visitor().SignedIn(user.Email, Password)).Session!;
        using var second = new BlazorHost(auth.Main, first.KeysDirectory);
        var rotated = await second.Visitor().WithCookie("gt_session", oldCookie).Get("/konto");
        var newCookie = rotated.SetCookie("gt_session")!.Split(';')[0]["gt_session=".Length..];

        // The OLD cookie in a process that has not seen the session: its refresh token was spent, authservice treats it as a replay and revokes the family.
        using var third = new BlazorHost(auth.Main, first.KeysDirectory);
        var replay = third.Visitor().WithCookie("gt_session", oldCookie);
        var page = await replay.Get("/konto");

        Assert.Equal(HttpStatusCode.Found, page.Status);
        Assert.StartsWith("/logowanie", page.Location);
        Assert.Null(replay.Session); // the response deletes the dead cookie
        using var fourth = new BlazorHost(auth.Main, first.KeysDirectory);
        var legit = await fourth.Visitor().WithCookie("gt_session", newCookie).Get("/konto");
        Assert.Equal(HttpStatusCode.Found, legit.Status); // the token the first refresh issued went with the family
        Assert.StartsWith("/logowanie", legit.Location);
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token_at_authservice_and_the_same_cookie_is_refused_everywhere()
    {
        using var host = new BlazorHost(auth.Main);
        var user = await auth.Main.Register();
        var visitor = await host.Visitor().SignedIn(user.Email, Password);
        var cookie = visitor.Session!;
        var refresh = host.RefreshTokenIn(cookie);
        Assert.Equal(HttpStatusCode.OK, (await visitor.Get("/konto")).Status);

        var page = await visitor.Submit("/wyloguj", "logout");

        Assert.Equal(HttpStatusCode.Found, page.Status);
        Assert.Null(visitor.Session);
        using var api = auth.Main.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.PostAsJsonAsync("api/v1/auth/refresh", new { refreshToken = refresh })).StatusCode);
        Assert.StartsWith("/logowanie", (await host.Visitor().WithCookie("gt_session", cookie).Get("/konto")).Location); // here: the cache marks the session dead
        using var after = new BlazorHost(auth.Main, host.KeysDirectory);
        Assert.StartsWith("/logowanie", (await after.Visitor().WithCookie("gt_session", cookie).Get("/konto")).Location); // a later process: authservice refuses the revoked token
    }

    [Fact]
    public async Task A_token_signed_by_a_rotated_key_is_accepted_after_the_host_reads_the_new_key_set()
    {
        using var oldKey = System.Security.Cryptography.RSA.Create(2048);
        var before = await auth.StartAsync("web-rotation-before", signingKeyPem: oldKey.ExportPkcs8PrivateKeyPem(), database: "webrotationdb");
        using var host = new BlazorHost(before, switchable: true);
        var user = await before.Register();
        var visitor = await host.Visitor().SignedIn(user.Email, Password); // the host has fetched and cached the key set with the old key only
        Assert.Equal(HttpStatusCode.OK, (await visitor.Get("/konto")).Status);

        // The same name now reaches the rotated service (docs/operations/AUTH.md section 4): same users, a new key, the old public key kept for tokens in flight.
        using var newKey = System.Security.Cryptography.RSA.Create(2048);
        var after = await auth.StartAsync("web-rotation-after", new Dictionary<string, string> { ["Jwt__PreviousPublicKeyPem"] = oldKey.ExportSubjectPublicKeyInfoPem() },
            signingKeyPem: newKey.ExportPkcs8PrivateKeyPem(), database: "webrotationdb");
        host.AuthTarget = new Uri(after.Url);

        host.Clock.Offset = TimeSpan.FromMinutes(59) + TimeSpan.FromSeconds(40); // forces a renewal; the new token names a key id the cached set does not have
        var page = await visitor.Get("/konto");
        host.Clock.Offset = TimeSpan.Zero;

        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Contains(user.Email, page.Text);
        var fresh = await host.Visitor().SignedIn(user.Email, Password); // and a new sign-in, signed by the new key, verifies
        Assert.Equal(HttpStatusCode.OK, (await fresh.Get("/konto")).Status);
    }

    // ---- /konto: me, consents, export, deletion -------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_account_page_reads_what_authservice_answers_and_the_export_is_a_download_of_the_users_own_data()
    {
        using var host = new BlazorHost(auth.Main);
        var user = await auth.Main.Register();
        var visitor = await host.Visitor().SignedIn(user.Email, Password);

        var page = await visitor.Get("/konto");

        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal(Pl.Account.Title, page.H1);
        Assert.Contains(user.Email, page.Text);
        Assert.Contains(Pl.Account.Verified, page.Text); // RequireConfirmedEmail=false: the address is confirmed
        Assert.Contains(Pl.Account.TermsAccepted, page.Text);
        Assert.Contains(Pl.Account.PrivacyAccepted, page.Text);
        Assert.Contains(user.Email, page.Text);
        Assert.Contains("delete-password", page.Html); // an account with a password is asked for it

        var export = await visitor.Get("/konto/export");
        Assert.Equal(HttpStatusCode.OK, export.Status);
        Assert.Contains("attachment", export.Header("Content-Disposition"));
        Assert.Equal("no-store", export.Header("Cache-Control"));
        Assert.Equal(user.Email, JsonDocument.Parse(export.Html).RootElement.GetProperty("profile").GetProperty("email").GetString());
        Assert.Equal(HttpStatusCode.Found, (await host.Visitor().Get("/konto/export")).Status); // a visitor is sent to sign in
    }

    [Fact]
    public async Task Deleting_the_account_a_wrong_password_does_not_delete_the_right_one_does_and_ends_the_session()
    {
        using var host = new BlazorHost(auth.Main);
        var user = await auth.Main.Register();
        var visitor = await host.Visitor().SignedIn(user.Email, Password);

        var wrong = await visitor.Submit("/konto", "delete-account", ("Delete.Password", Password + "-wrong"), ("Delete.Confirm", "true"));
        Assert.Equal(HttpStatusCode.OK, wrong.Status);
        Assert.Equal(Pl.Account.DeleteFailed, wrong.Alert);
        Assert.NotNull(visitor.Session);
        Assert.Equal(HttpStatusCode.Found, (await host.Visitor().SignIn(user.Email, Password)).Status); // still there

        var unconfirmed = await visitor.Submit("/konto", "delete-account", ("Delete.Password", Password));
        Assert.Equal(Pl.Account.DeleteConfirmRequired, unconfirmed.Alert);

        var done = await visitor.Submit("/konto", "delete-account", ("Delete.Password", Password), ("Delete.Confirm", "true"));
        Assert.Equal(HttpStatusCode.Found, done.Status);
        Assert.Equal("/", done.Location);
        Assert.Null(visitor.Session);
        Assert.Equal(Pl.Login.InvalidCredentials, (await host.Visitor().SignIn(user.Email, Password)).Alert); // the account is gone
    }

    // ---- the administrator role ------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_seeded_SuperAdmin_one_role_as_a_string_reaches_the_admin_area()
    {
        using var host = new BlazorHost(auth.Main);
        var owner = await auth.Main.LoginOwner();
        Assert.Equal(JsonValueKind.String, owner.Jwt.Payload.GetProperty(WebApp::Gabinety.Web.Auth.ClaimMapping.RoleClaimUri).ValueKind); // the shape under test: a string, not an array
        var visitor = await host.Visitor().SignedIn(owner.Email, owner.Password);

        var page = await visitor.Get("/admin");

        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal(PlAdmin.Shell.Title, page.H1);
        Assert.Equal(["SuperAdmin"], host.RolesIn(visitor.Session!));
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Admin", "User")]
    public async Task An_Admin_role_as_a_string_or_in_an_array_reaches_the_admin_area(params string[] roles)
    {
        using var host = new BlazorHost(auth.Main);
        var user = await auth.Main.UserWithRoles(roles);
        Assert.Equal(roles.Length == 1 ? JsonValueKind.String : JsonValueKind.Array, user.Jwt.Payload.GetProperty(WebApp::Gabinety.Web.Auth.ClaimMapping.RoleClaimUri).ValueKind);
        var visitor = await host.Visitor().SignedIn(user.Email, user.Password);

        var page = await visitor.Get("/admin");

        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal(PlAdmin.Shell.Title, page.H1);
        Assert.Equal(roles.OrderBy(r => r), host.RolesIn(visitor.Session!).OrderBy(r => r));
    }

    [Fact]
    public async Task A_plain_user_gets_the_403_page_the_platform_role_User_alone_does_too_and_a_visitor_is_sent_to_sign_in()
    {
        using var host = new BlazorHost(auth.Main);
        var plain = await auth.Main.Register();
        var userRole = await auth.Main.UserWithRoles("User");

        foreach (var user in new[] { plain, userRole })
        {
            var visitor = await host.Visitor().SignedIn(user.Email, user.Password);
            var page = await visitor.Get("/admin");
            Assert.Equal(HttpStatusCode.Forbidden, page.Status);
            Assert.Contains(PlAdmin.Forbidden.Title, page.Text);
            Assert.DoesNotContain(PlAdmin.Shell.Title, page.H1);
            Assert.Equal(HttpStatusCode.Forbidden, (await visitor.Get("/admin/ksiega/csv?from=2026-01-01&to=2026-01-31")).Status); // the endpoint that is not a page
        }

        var anonymous = await host.Visitor().Get("/admin");
        Assert.Equal(HttpStatusCode.Found, anonymous.Status);
        Assert.Equal("/logowanie?redirect=%2Fadmin", anonymous.Location);
    }

    // ---- GitHub: the start only ----------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_GitHub_start_leaves_a_state_cookie_and_a_URL_authservice_answers_with_a_redirect_to_github()
    {
        using var host = new BlazorHost(auth.Main);
        var visitor = host.Visitor();

        var start = await visitor.Get("/oauth/github/start?redirect=%2Fmoje%2Frezerwacje");

        Assert.Equal(HttpStatusCode.Found, start.Status);
        var cookie = start.SetCookie("gt_oauth_state")!;
        Assert.NotNull(cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        var state = cookie.Split(';')[0]["gt_oauth_state=".Length..];
        Assert.Matches("^[A-Za-z0-9_-]{40,}$", state);
        Assert.StartsWith(auth.Main.Url + "/api/v1/external-auth/login?provider=GitHub&returnUrl=", start.Location);
        var returnUrl = System.Web.HttpUtility.ParseQueryString(new Uri(start.Location!).Query)["returnUrl"]!;
        Assert.StartsWith($"{RealAuthFixture.WebOrigin}/oauth/callback?redirect=%2Fmoje%2Frezerwacje&state=" + state, returnUrl);

        // What authservice does with exactly that URL: a redirect to GitHub's authorize page. (It would not accept it if the origin, the path or the provider were off.)
        using var http = auth.Main.Client();
        var onward = await http.GetAsync(start.Location);
        Assert.Equal(HttpStatusCode.Redirect, onward.StatusCode);
        var target = onward.Headers.Location!;
        Assert.Equal("https://github.com/login/oauth/authorize", target.GetLeftPart(UriPartial.Path));
        Assert.False(string.IsNullOrEmpty(System.Web.HttpUtility.ParseQueryString(target.Query)["state"]));
    }

    [Fact]
    public async Task The_callback_refuses_a_code_without_the_matching_state_and_an_exchange_code_authservice_never_issued()
    {
        // The callback leg of a REAL GitHub sign-in cannot run here: it needs a real OAuth app (T-09.01). What can: the host's own checks and authservice's answer to a code it never issued.
        using var host = new BlazorHost(auth.Main);

        var noCookie = await host.Visitor().Get("/oauth/callback?code=never-issued&state=abc");
        Assert.Equal("/logowanie?error=github_failed", noCookie.Location);

        var visitor = host.Visitor();
        var start = await visitor.Get("/oauth/github/start");
        var state = start.SetCookie("gt_oauth_state")!.Split(';')[0]["gt_oauth_state=".Length..];
        var mismatch = await visitor.Get($"/oauth/callback?code=never-issued&state={state}x");
        Assert.Equal("/logowanie?error=github_failed", mismatch.Location);
        Assert.Null(visitor.Cookies.GetValueOrDefault("gt_oauth_state")); // single use, whatever the outcome

        var second = host.Visitor();
        var again = await second.Get("/oauth/github/start");
        var good = again.SetCookie("gt_oauth_state")!.Split(';')[0]["gt_oauth_state=".Length..];
        var exchanged = await second.Get($"/oauth/callback?code=never-issued&state={good}");
        Assert.Equal("/logowanie?error=github_failed", exchanged.Location); // authservice answers 401 to the code: no session
        Assert.Null(second.Session);
        var login = await second.Get(exchanged.Location!);
        Assert.Equal(Pl.Login.GithubFailed, login.Alert);
    }
}
