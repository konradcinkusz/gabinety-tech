using System.Net;

namespace Gabinety.Web.Tests.Pipeline;

/// <summary>
/// SPEC 5 rule 13: the only cookies are the session cookies of signed-in users; the public site needs no cookie banner. Found by running the real host: every component page issued an antiforgery
/// cookie (and Cache-Control: no-store) even with no form on it. A page without a form opts out of the token; a page with one keeps it.
/// </summary>
public class AnonymousCookieTests
{
    static WebFactory Web()
    {
        var web = new WebFactory();
        web.Directory.Respond = r => r.RequestUri!.AbsolutePath switch
        {
            "/api/v1/taxonomy" => FakeBackend.Json(Sample.Taxonomy),
            "/api/v1/sitemap" => FakeBackend.Json(Sample.Sitemap()),
            _ => FakeBackend.Json(Sample.Cards(2)),
        };
        return web;
    }

    [Theory]
    [InlineData("/")] [InlineData("/programisci")] [InlineData("/programisci/audyt-kodu")] [InlineData("/programisci/nie-ma")] [InlineData("/nie-ma-takiej-strony")] [InlineData("/not-found")] [InlineData("/Error")]
    public async Task A_public_page_sets_no_cookie_and_is_not_marked_no_store(string path)
    {
        await using var web = Web();
        var page = await web.NewBrowser().GetAsync(path);
        Assert.Empty(page.SetCookies);
        Assert.DoesNotContain("no-store", page.Header("Cache-Control") ?? "");
    }

    [Theory]
    [InlineData("/health")] [InlineData("/alive")] [InlineData("/oauth/callback?code=a&state=b")] // the callback is a no-store redirect by design, and sets nothing without a state cookie
    public async Task The_probes_set_no_cookie(string path)
    {
        await using var web = Web();
        Assert.Empty((await web.NewBrowser().GetAsync(path)).SetCookies); // they are no-store by design; a cookie on a probe would be a bug
    }

    [Fact]
    public async Task Every_public_page_of_the_list_stays_cookie_free_with_a_search_a_filter_and_a_cursor()
    {
        await using var web = Web();
        var browser = web.NewBrowser();
        foreach (var path in new[] { "/programisci?q=kod", "/programisci/audyt-kodu?duration=30", "/programisci?cursor=abc" })
        {
            var page = await browser.GetAsync(path);
            Assert.DoesNotContain(page.SetCookies, c => true);
        }
        Assert.Empty(browser.Cookies);
    }

    [Fact]
    public async Task The_pages_with_a_form_keep_their_antiforgery_token_and_it_is_the_only_cookie()
    {
        await using var web = Web();
        foreach (var path in new[] { "/logowanie", "/wyloguj" })
        {
            var page = await web.NewBrowser().GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, page.Status);
            var cookie = Assert.Single(page.SetCookies);
            Assert.StartsWith(".AspNetCore.Antiforgery.", cookie);
            Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
            Assert.NotNull(page.One("input[name=__RequestVerificationToken]"));
            Assert.Contains("no-store", page.Header("Cache-Control")); // a page that carries a token must not be cached
        }
    }

    [Fact]
    public async Task A_forged_form_post_without_a_token_is_refused_by_antiforgery_on_the_pages_that_take_one()
    {
        await using var web = Web();
        var response = await web.Client().PostAsync("/logowanie", new FormUrlEncodedContent(new Dictionary<string, string> { ["_handler"] = "login", ["Model.Email"] = "a@b.c", ["Model.Password"] = "x" }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(web.Identity.Backend.Requests); // never reached the identity service
    }
}

static class FactoryExtensions
{
    public static HttpClient Client(this WebFactory web) => web.CreateClient(new() { AllowAutoRedirect = false });
}
