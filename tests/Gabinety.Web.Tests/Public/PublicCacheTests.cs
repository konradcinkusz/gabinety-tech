using System.Net;
using Gabinety.Web.Api;
using Gabinety.Web.Hosting;

namespace Gabinety.Web.Tests.Public;

/// <summary>
/// The output cache of the public pages (ADR-0010 cutover precondition): five minutes, by route and query, only for an anonymous GET, never with a session or auth cookie, never for a 4xx/5xx,
/// switchable off. Each rule is one test; the backend's call count is the proof that a response came from the cache.
/// </summary>
public class PublicCacheTests
{
    static int Reads(WebFactory web) => web.Directory.Requests.Count(r => r.PathAndQuery == "/api/v1/taxonomy");

    [Fact]
    public void The_default_is_five_minutes_and_the_key_overrides_it()
    {
        static PublicPageCachePolicy With(params (string, string?)[] kv) => new(new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(kv.Select(p => KeyValuePair.Create(p.Item1, p.Item2))).Build());
        Assert.Equal(TimeSpan.FromMinutes(5), With().Duration);
        Assert.Equal(TimeSpan.FromSeconds(30), With(("OutputCache:Seconds", "30")).Duration);
    }

    [Theory]
    [InlineData("/")] [InlineData("/programisci")] [InlineData("/programisci/audyt-kodu")] [InlineData("/gabinet/demo-anna-demowska")] [InlineData("/jak-to-dziala")] [InlineData("/cennik")]
    [InlineData("/zasady-rankingu")] [InlineData("/regulamin")] [InlineData("/polityka-prywatnosci")] [InlineData("/sitemap.xml")] [InlineData("/robots.txt")]
    public async Task An_anonymous_get_of_a_public_page_is_served_from_the_cache_the_second_time(string path)
    {
        await using var web = PublicSupport.Host();
        var browser = web.NewBrowser();
        var first = await browser.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, first.Status);
        var calls = web.Directory.Calls + web.Booking.Calls + web.Reputation.Calls;
        var second = await browser.GetAsync(path);
        Assert.Equal(first.Html, second.Html);
        Assert.Equal(calls, web.Directory.Calls + web.Booking.Calls + web.Reputation.Calls); // no backend was asked again
    }

    [Fact]
    public async Task The_key_is_route_and_query_so_a_filter_is_its_own_entry()
    {
        await using var web = PublicSupport.Host();
        var browser = web.NewBrowser();
        await browser.GetAsync("/programisci");
        var after = web.Directory.Calls;
        await browser.GetAsync("/programisci?duration=30");
        Assert.True(web.Directory.Calls > after); // another query: not the cached plain list
        var filtered = web.Directory.Calls;
        await browser.GetAsync("/programisci?duration=30");
        await browser.GetAsync("/programisci");
        Assert.Equal(filtered, web.Directory.Calls); // both now cached
        await browser.GetAsync("/programisci?duration=60");
        Assert.True(web.Directory.Calls > filtered); // a third query is a third entry
        await browser.GetAsync("/programisci/audyt-kodu");
        Assert.Contains(web.Directory.Requests, r => r.PathAndQuery.StartsWith("/api/v1/practitioners?problem=audyt-kodu")); // another route too
    }

    [Fact]
    public async Task The_profiles_picker_state_is_part_of_the_key()
    {
        await using var web = PublicSupport.Host();
        var browser = web.NewBrowser();
        var plain = await browser.GetAsync("/gabinet/demo-anna-demowska");
        var day = await browser.GetAsync($"/gabinet/demo-anna-demowska?oferta={PublicSupport.OfferA}&dzien=2026-11-25");
        Assert.NotEqual(plain.Html, day.Html);
    }

    [Fact]
    public async Task A_request_with_the_session_cookie_is_never_served_from_or_stored_in_the_cache()
    {
        await using var web = PublicSupport.Host();
        var browser = web.NewBrowser();
        await browser.GetAsync("/"); // an anonymous visitor warms the cache
        var warm = web.Directory.Calls;
        await browser.GetAsync("/", ("Cookie", $"{AuthenticationExtensions.CookieName}=anything"));
        Assert.True(web.Directory.Calls > warm); // the person with a session was NOT served the anonymous copy
        var afterSession = web.Directory.Calls;
        await browser.GetAsync("/", ("Cookie", $"{AuthenticationExtensions.CookieName}=anything"));
        Assert.True(web.Directory.Calls > afterSession); // and a second request with a session is not served from a stored copy either
    }

    [Fact]
    public async Task A_page_rendered_for_a_session_never_reaches_an_anonymous_visitor()
    {
        await using var web = PublicSupport.Host();
        var browser = web.NewBrowser();
        await browser.GetAsync("/programisci?q=x", ("Cookie", $"{AuthenticationExtensions.CookieName}=anything")); // first: with a session
        var calls = web.Directory.Calls;
        await browser.GetAsync("/programisci?q=x"); // then anonymous: must render, because nothing was stored
        Assert.True(web.Directory.Calls > calls);
    }

    [Fact]
    public async Task An_Authorization_header_is_never_cached()
    {
        await using var web = PublicSupport.Host();
        var browser = web.NewBrowser();
        await browser.GetAsync("/zasady-rankingu", ("Authorization", "Bearer x"));
        var calls = web.Directory.Calls;
        await browser.GetAsync("/zasady-rankingu", ("Authorization", "Bearer x"));
        Assert.True(web.Directory.Calls > calls);
    }

    [Theory]
    [InlineData("POST")] [InlineData("PUT")] [InlineData("DELETE")]
    public void Only_a_get_or_head_is_cacheable(string method)
    {
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext { Request = { Method = method } };
        Assert.False(PublicPageCachePolicy.IsAnonymousRead(context.Request));
        context.Request.Method = "GET";
        Assert.True(PublicPageCachePolicy.IsAnonymousRead(context.Request));
        context.Request.Method = "HEAD";
        Assert.True(PublicPageCachePolicy.IsAnonymousRead(context.Request));
    }

    [Fact]
    public async Task A_404_is_never_stored()
    {
        await using var web = PublicSupport.Host();
        var browser = web.NewBrowser();
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync("/gabinet/nie-ma")).Status);
        var calls = web.Directory.Calls;
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync("/gabinet/nie-ma")).Status);
        Assert.True(web.Directory.Calls > calls);
        // ... and when the profile appears, the very next request sees it: a missing page is not remembered for five minutes.
        web.Directory.Respond = PublicSupport.DefaultDirectory().Let(d => (Func<HttpRequestMessage, HttpResponseMessage>)(r => r.RequestUri!.AbsolutePath == "/api/v1/practitioners/nie-ma" ? FakeBackend.Json(PublicSupport.Profile()) : d(r)));
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/gabinet/nie-ma")).Status);
    }

    [Fact]
    public async Task A_503_is_never_stored_so_a_recovered_directory_is_seen_at_once()
    {
        await using var web = PublicSupport.Host(FakeBackend.Throw(new HttpRequestException("down")));
        var browser = web.NewBrowser();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await browser.GetAsync("/zasady-rankingu")).Status);
        web.Directory.Respond = PublicSupport.DefaultDirectory();
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/zasady-rankingu")).Status);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await PublicSupport.Host(FakeBackend.Throw(new HttpRequestException("down"))).NewBrowser().GetAsync("/sitemap.xml")).Status);
    }

    [Fact]
    public async Task A_410_and_a_redirect_style_error_are_never_stored()
    {
        await using var web = PublicSupport.Host(r => r.RequestUri!.AbsolutePath.StartsWith("/api/v1/practitioners/") ? FakeBackend.Problem(HttpStatusCode.Gone, "gone") : PublicSupport.DefaultDirectory()(r));
        var browser = web.NewBrowser();
        Assert.Equal(HttpStatusCode.Gone, (await browser.GetAsync("/gabinet/demo-anna-demowska")).Status);
        var calls = web.Directory.Calls;
        await browser.GetAsync("/gabinet/demo-anna-demowska");
        Assert.True(web.Directory.Calls > calls);
    }

    [Fact]
    public async Task The_cache_can_be_turned_off_for_the_E2E_host()
    {
        await using var web = PublicSupport.Host();
        web.With("OutputCache:Enabled", "false");
        var browser = web.NewBrowser();
        await browser.GetAsync("/");
        var calls = web.Directory.Calls;
        await browser.GetAsync("/");
        Assert.True(web.Directory.Calls > calls);
    }

    [Fact]
    public async Task An_entry_expires_after_the_configured_time()
    {
        await using var web = PublicSupport.Host();
        web.With("OutputCache:Seconds", "1");
        var browser = web.NewBrowser();
        await browser.GetAsync("/");
        var calls = web.Directory.Calls;
        await Task.Delay(TimeSpan.FromSeconds(1.5));
        await browser.GetAsync("/");
        Assert.True(web.Directory.Calls > calls);
    }

    [Fact]
    public async Task A_cached_response_still_carries_the_security_headers_and_no_cookie()
    {
        await using var web = PublicSupport.Host();
        var browser = web.NewBrowser();
        await browser.GetAsync("/cennik");
        var second = await browser.GetAsync("/cennik");
        Assert.Empty(second.SetCookies);
        Assert.Contains("script-src 'self'", second.Header("Content-Security-Policy"));
        Assert.Equal("nosniff", second.Header("X-Content-Type-Options"));
    }

    [Fact]
    public async Task The_cache_is_only_on_the_public_pages_the_sign_in_and_the_session_pages_are_never_cached()
    {
        // A route-level proof: only the endpoints that name the policy carry it.
        await using var web = PublicSupport.Host();
        web.CreateClient();
        var cached = web.Services.GetServices<Microsoft.AspNetCore.Routing.EndpointDataSource>().SelectMany(s => s.Endpoints).OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
            .Where(e => e.Metadata.OfType<Microsoft.AspNetCore.OutputCaching.OutputCacheAttribute>().Any(a => a.PolicyName == PublicPageCachePolicy.Name) || e.Metadata.GetMetadata<Microsoft.AspNetCore.OutputCaching.IOutputCachePolicy>() is not null)
            .Select(e => e.RoutePattern.RawText).Order().ToList();
        Assert.DoesNotContain("/logowanie", cached);
        Assert.DoesNotContain("/wyloguj", cached);
        Assert.DoesNotContain("/moje/rezerwacje", cached);
        Assert.DoesNotContain("/oauth/callback", cached);
        Assert.Contains("/gabinet/{Slug}", cached);
        Assert.Contains("/sitemap.xml", cached);
    }
}

static class Fn { public static R Let<T, R>(this T value, Func<T, R> f) => f(value); }
