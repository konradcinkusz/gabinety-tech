using System.Net;
using Gabinety.Web.Api;
using Gabinety.Web.Auth;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;

namespace Gabinety.Web.Tests.Pipeline;

/// <summary>
/// ADR-0010 cutover precondition: a deploy must not sign users out. The session is a sealed cookie (Data Protection, a persisted shared key ring), the access token is re-minted from its refresh
/// token, and the rotation of that single-use token is safe under concurrency. A "deploy" here is a SECOND host process (a new factory: empty memory, same key ring directory, same identity service).
/// </summary>
public sealed class StatelessSessionTests : IDisposable
{
    readonly DirectoryInfo _keys = Directory.CreateTempSubdirectory("gt-keys-");
    public void Dispose() { try { _keys.Delete(true); } catch (IOException) { } }

    WebFactory Process(FakeIdentity? identity = null, string? keysPath = null)
    {
        var web = new WebFactory().SharingIdentity(identity).With("DataProtection:KeysPath", keysPath ?? _keys.FullName);
        web.Booking.Respond = r => r.Headers.Authorization is null // the real service: no bearer, no bookings
            ? FakeBackend.Problem(HttpStatusCode.Unauthorized, "unauthenticated")
            : FakeBackend.Json(new PageDto<BookingViewDto>([Sample.Booking("Konsultacja demo")], null));
        return web;
    }

    static Dictionary<string, string> Form() => new() { ["Model.Email"] = "demo-client@demo.example", ["Model.Password"] = FakeIdentity.Password };

    static Browser WithCookie(WebFactory web, string value) => web.NewBrowser().WithCookie("gt_session", value);

    [Fact]
    public async Task The_cookie_is_sealed_holds_no_token_and_stays_under_the_size_budget_even_for_an_administrator_with_a_long_address()
    {
        await using var web = Process();
        web.Identity.Roles = ["Admin", "SuperAdmin", "User", "Moderator"];
        var email = new string('a', 120) + "@" + new string('b', 120) + ".example";
        var page = await web.NewBrowser().PostFormAsync("/logowanie", new Dictionary<string, string> { ["Model.Email"] = email, ["Model.Password"] = FakeIdentity.Password });
        Assert.Equal(HttpStatusCode.Found, page.Status);
        var cookies = page.SetCookies.Where(c => c.StartsWith("gt_session")).ToList();
        Assert.Single(cookies); // not chunked
        var value = cookies[0].Split(';')[0]["gt_session=".Length..];
        Assert.InRange(cookies[0].Length, 1, 3500); // the budget: far from the 4096 bytes a browser keeps
        Assert.DoesNotContain("eyJ", value);
        Assert.DoesNotContain("refresh-", value);
        Assert.DoesNotContain("demo-client", Uri.UnescapeDataString(value)); // sealed, not just encoded
    }

    [Fact]
    public async Task A_deploy_does_not_sign_the_user_out_the_new_process_mints_the_access_token_from_the_cookie()
    {
        var identity = new FakeIdentity();
        string cookie;
        await using (var before = Process(identity))
        {
            var browser = before.NewBrowser();
            cookie = await SignIn.As(before, browser);
        }
        await using var after = Process(identity); // empty memory, same key ring
        var page = await WithCookie(after, cookie).GetAsync("/moje/rezerwacje");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal("Moje rezerwacje", page.TextOf("h1"));
        Assert.Equal(["refresh-1"], identity.SpentRefreshTokens); // exactly the refresh token the sign-in put in the cookie, once
        Assert.StartsWith("Bearer eyJ", after.Booking.Requests.Last().Authorization);
        Assert.Equal(1, identity.RefreshCalls);
    }

    [Fact]
    public async Task A_process_with_another_key_ring_cannot_read_the_cookie_the_failure_mode_the_shared_ring_prevents()
    {
        var identity = new FakeIdentity();
        string cookie;
        await using (var before = Process(identity)) cookie = await SignIn.As(before, before.NewBrowser());
        var other = Directory.CreateTempSubdirectory("gt-other-keys-");
        try
        {
            await using var after = Process(identity, other.FullName);
            var page = await WithCookie(after, cookie).GetAsync("/moje/rezerwacje");
            Assert.Equal(HttpStatusCode.Found, page.Status);
            Assert.StartsWith("/logowanie?redirect=", page.Location);
        }
        finally { other.Delete(true); }
    }

    [Fact]
    public async Task A_key_added_to_the_ring_later_does_not_invalidate_a_cookie_sealed_with_an_older_key()
    {
        var identity = new FakeIdentity();
        string cookie;
        await using (var before = Process(identity))
        {
            cookie = await SignIn.As(before, before.NewBrowser());
            var manager = before.Services.GetRequiredService<IKeyManager>();
            var count = manager.GetAllKeys().Count;
            manager.CreateNewKey(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(90)); // the rotation the framework does every 90 days
            Assert.Equal(count + 1, manager.GetAllKeys().Count);
        }
        await using var after = Process(identity); // loads both keys; the new one is the default for sealing
        Assert.Equal(2, after.Services.GetRequiredService<IKeyManager>().GetAllKeys().Count);
        Assert.Equal(HttpStatusCode.OK, (await WithCookie(after, cookie).GetAsync("/moje/rezerwacje")).Status);
    }

    [Fact]
    public async Task A_revoked_key_no_longer_decrypts_so_the_ring_must_be_kept_for_the_cookie_lifetime()
    {
        var identity = new FakeIdentity();
        string cookie;
        await using (var before = Process(identity))
        {
            cookie = await SignIn.As(before, before.NewBrowser());
            before.Services.GetRequiredService<IKeyManager>().RevokeAllKeys(DateTimeOffset.UtcNow, "test"); // the operator's decision, not a rotation
        }
        await using var after = Process(identity);
        Assert.Equal(HttpStatusCode.Found, (await WithCookie(after, cookie).GetAsync("/moje/rezerwacje")).Status);
    }

    [Theory]
    [InlineData("flip")] [InlineData("truncate")] [InlineData("append")] [InlineData("empty-payload")]
    public async Task A_tampered_cookie_is_signed_out_and_asks_nothing_of_the_identity_service(string how)
    {
        await using var web = Process();
        var cookie = await SignIn.As(web, web.NewBrowser());
        var calls = web.Identity.Backend.Calls;
        var bad = how switch
        {
            "flip" => cookie[..^10] + (cookie[^10] == 'A' ? 'B' : 'A') + cookie[^9..],
            "truncate" => cookie[..(cookie.Length / 2)],
            "append" => cookie + "AAAA",
            _ => "CfDJ8",
        };
        var page = await WithCookie(web, bad).GetAsync("/moje/rezerwacje");
        Assert.Equal(HttpStatusCode.Found, page.Status);
        Assert.StartsWith("/logowanie?redirect=", page.Location);
        Assert.Equal(calls, web.Identity.Backend.Calls);
    }

    [Fact]
    public async Task A_rotation_is_written_into_the_cookie_so_a_deploy_after_it_still_works_and_the_spent_cookie_does_not()
    {
        var identity = new FakeIdentity { AccessLifetime = TimeSpan.FromSeconds(10) }; // the sign-in token is about to expire
        string first, second;
        await using (var before = Process(identity))
        {
            var browser = before.NewBrowser();
            first = await SignIn.As(before, browser);
            identity.AccessLifetime = TimeSpan.FromHours(1); // what the rotation hands out
            var page = await browser.GetAsync("/moje/rezerwacje");
            Assert.Equal(HttpStatusCode.OK, page.Status);
            Assert.Equal(1, identity.RefreshCalls); // the sign-in token was about to expire: one rotation
            Assert.Contains(page.SetCookies, c => c.StartsWith("gt_session=") && !c.StartsWith("gt_session=;")); // the new refresh token rides on this response
            second = browser.Cookies["gt_session"];
            Assert.NotEqual(first, second);
        }
        await using var after = Process(identity);
        Assert.Equal(HttpStatusCode.OK, (await WithCookie(after, second).GetAsync("/moje/rezerwacje")).Status); // the newest refresh token: accepted
        await using var replay = Process(identity);
        var spent = await WithCookie(replay, first).GetAsync("/moje/rezerwacje"); // the one already spent: authservice refuses it (and revokes the family)
        Assert.Contains("Sesja wygasła", spent.Text);
        Assert.Contains(spent.SetCookies, c => c.StartsWith("gt_session=;")); // the dead cookie is deleted
    }

    [Fact]
    public async Task Simultaneous_requests_after_a_deploy_spend_the_refresh_token_once()
    {
        var identity = new FakeIdentity();
        string cookie;
        await using (var before = Process(identity)) cookie = await SignIn.As(before, before.NewBrowser());
        await using var after = Process(identity);
        var pages = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => WithCookie(after, cookie).GetAsync("/moje/rezerwacje")));
        Assert.All(pages, p => Assert.Equal(HttpStatusCode.OK, p.Status));
        Assert.Equal(1, identity.RefreshCalls);
        Assert.Equal(identity.SpentRefreshTokens.Distinct().Count(), identity.SpentRefreshTokens.Count); // never twice: a replay would have revoked the session
    }

    [Theory]
    [InlineData(429)] [InlineData(503)]
    public async Task A_throttled_or_failing_refresh_does_not_end_the_session(int status)
    {
        var identity = new FakeIdentity { AccessLifetime = TimeSpan.FromSeconds(10) };
        await using var web = Process(identity);
        var browser = web.NewBrowser();
        await SignIn.As(web, browser);
        identity.RefreshStatus = status;
        var page = await browser.GetAsync("/moje/rezerwacje");
        Assert.Equal(HttpStatusCode.OK, page.Status); // the old token still lasts a few seconds
        Assert.DoesNotContain(page.SetCookies, c => c.StartsWith("gt_session=;")); // and the cookie is kept
        identity.RefreshStatus = 0;
        Assert.True(browser.Cookies.ContainsKey("gt_session"));
    }

    [Fact]
    public async Task Signing_out_revokes_at_the_identity_service_so_the_cookie_is_worthless_even_after_a_deploy()
    {
        var identity = new FakeIdentity();
        string cookie;
        await using (var before = Process(identity))
        {
            var browser = before.NewBrowser();
            cookie = await SignIn.As(before, browser);
            await browser.PostFormAsync("/wyloguj", new Dictionary<string, string>());
            Assert.Contains(before.Identity.Backend.Requests, r => r.PathAndQuery == "/api/v1/auth/logout" && r.Authorization!.StartsWith("Bearer eyJ"));
        }
        await using var after = Process(identity); // no memory of the sign-out: the identity service refuses the refresh token
        var page = await WithCookie(after, cookie).GetAsync("/moje/rezerwacje");
        var again = WithCookie(after, cookie);
        Assert.Contains("Sesja wygasła", page.Text); // the refresh token is revoked: the page says the session ended...
        Assert.Contains(page.SetCookies, c => c.StartsWith("gt_session=;")); // ...and the dead cookie is deleted with the same response
        Assert.Equal(HttpStatusCode.Found, (await again.GetAsync("/moje/rezerwacje")).Status); // while the cookie is still sent the host refuses it at once
        Assert.Single(after.Identity.Backend.Requests, r => r.PathAndQuery.Contains("/auth/refresh")); // without asking the identity service again
    }

    [Fact]
    public async Task Signing_out_after_a_deploy_still_reaches_the_identity_service_with_a_fresh_access_token()
    {
        var identity = new FakeIdentity();
        string cookie;
        await using (var before = Process(identity)) cookie = await SignIn.As(before, before.NewBrowser());
        await using var after = Process(identity);
        var browser = WithCookie(after, cookie);
        var response = await browser.PostFormAsync("/wyloguj", new Dictionary<string, string>());
        Assert.Equal(HttpStatusCode.Found, response.Status);
        Assert.True(identity.Revoked);
    }
}
