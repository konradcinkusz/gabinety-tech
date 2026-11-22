using System.Net;
using System.Text.Json;

namespace Gabinety.Web.Tests.Pipeline;

/// <summary>
/// BLAZOR-WEB-APP section 8, the pins, on the real host: a foreign Origin cannot negotiate a circuit, is refused before the websocket upgrade, Sec-Fetch-Site: cross-site is refused, and a link
/// followed from another site still loads the page. Plus journey J7's expectations: 403 with the code cross_site_request, and nothing changes; the control with the site's own Origin passes.
/// </summary>
public class OriginGuardHostTests
{
    const string Own = "http://localhost", Foreign = "https://evil.example";

    static async Task<(HttpStatusCode Status, string? Code)> Send(WebFactory web, HttpMethod method, string path, params (string, string)[] headers)
    {
        using var request = new HttpRequestMessage(method, path);
        foreach (var (n, v) in headers) request.Headers.TryAddWithoutValidation(n, v);
        var response = await web.CreateClient(new() { AllowAutoRedirect = false }).SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        string? code = null;
        if (response.Content.Headers.ContentType?.MediaType == "application/problem+json") code = JsonDocument.Parse(body).RootElement.GetProperty("code").GetString();
        return (response.StatusCode, code);
    }

    [Fact]
    public async Task A_foreign_Origin_cannot_negotiate_a_circuit()
    {
        await using var web = new WebFactory();
        var (status, code) = await Send(web, HttpMethod.Post, "/_blazor/negotiate?negotiateVersion=1", ("Origin", Foreign));
        Assert.Equal((HttpStatusCode.Forbidden, "cross_site_request"), (status, code));
    }

    [Fact]
    public async Task A_foreign_Origin_is_refused_before_the_websocket_upgrade()
    {
        await using var web = new WebFactory();
        var (status, code) = await Send(web, HttpMethod.Get, "/_blazor?id=abc", ("Origin", Foreign), ("Connection", "Upgrade"), ("Upgrade", "websocket"), ("Sec-WebSocket-Version", "13"), ("Sec-WebSocket-Key", "dGhlIHNhbXBsZSBub25jZQ=="));
        Assert.Equal((HttpStatusCode.Forbidden, "cross_site_request"), (status, code)); // not 101: the guard answers before the hub sees the handshake
    }

    [Fact]
    public async Task Sec_Fetch_Site_cross_site_is_refused_even_without_an_Origin()
    {
        await using var web = new WebFactory();
        var (status, code) = await Send(web, HttpMethod.Post, "/_blazor/negotiate?negotiateVersion=1", ("Sec-Fetch-Site", "cross-site"), ("Sec-Fetch-Mode", "cors"), ("Sec-Fetch-Dest", "empty"));
        Assert.Equal((HttpStatusCode.Forbidden, "cross_site_request"), (status, code));
    }

    [Fact]
    public async Task A_link_followed_from_another_site_loads_the_page()
    {
        await using var web = new WebFactory();
        web.Directory.Respond = _ => FakeBackend.Json(Sample.Taxonomy);
        var (status, _) = await Send(web, HttpMethod.Get, "/", ("Sec-Fetch-Site", "cross-site"), ("Sec-Fetch-Mode", "navigate"), ("Sec-Fetch-Dest", "document"));
        Assert.Equal(HttpStatusCode.OK, status);
    }

    [Fact]
    public async Task Requests_with_the_sites_own_origin_or_with_no_origin_pass_the_guard()
    {
        await using var web = new WebFactory();
        // The hub answers the negotiation of a same-origin client (and so is anonymous: an interactive public page needs it).
        Assert.Equal(HttpStatusCode.OK, (await Send(web, HttpMethod.Post, "/_blazor/negotiate?negotiateVersion=1", ("Origin", Own), ("Sec-Fetch-Site", "same-origin"))).Status);
        Assert.Equal(HttpStatusCode.OK, (await Send(web, HttpMethod.Post, "/_blazor/negotiate?negotiateVersion=1")).Status); // curl, health probes, Playwright's request context
        Assert.Equal(HttpStatusCode.OK, (await Send(web, HttpMethod.Get, "/health")).Status); // a probe carries no Origin
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(web, HttpMethod.Get, "/health", ("Origin", Foreign))).Status); // and the refusal is for every path, not only for the circuit
    }

    [Fact]
    public async Task J7_a_foreign_Origin_or_a_cross_site_post_to_a_form_is_403_with_the_code_and_creates_no_session_and_the_same_origin_control_passes()
    {
        await using var web = new WebFactory();
        var browser = web.NewBrowser();
        var form = new Dictionary<string, string> { ["Model.Email"] = "demo-client@demo.example", ["Model.Password"] = FakeIdentity.Password };

        foreach (var (what, headers) in new (string, (string, string)[])[]
        {
            ("foreign Origin", [("Origin", Foreign)]),
            ("Sec-Fetch-Site: cross-site", [("Sec-Fetch-Site", "cross-site")]),
            ("foreign Origin and cross-site", [("Origin", Foreign), ("Sec-Fetch-Site", "cross-site")]),
        })
        {
            var response = await browser.PostFormAsync("/logowanie", form, headers: headers);
            Assert.True(response.Status == HttpStatusCode.Forbidden, what);
            Assert.Equal("cross_site_request", JsonDocument.Parse(response.Html).RootElement.GetProperty("code").GetString());
            Assert.Empty(response.SetCookies);
            Assert.DoesNotContain(browser.Cookies.Keys, k => k == "gt_session");
        }
        Assert.DoesNotContain(web.Identity.Backend.Requests, r => r.PathAndQuery.Contains("/auth/login")); // nothing reached the identity service

        var control = await browser.PostFormAsync("/logowanie", form, headers: [("Origin", Own), ("Sec-Fetch-Site", "same-origin")]);
        Assert.Equal(HttpStatusCode.Found, control.Status);
        Assert.Contains("gt_session", browser.Cookies.Keys);
    }

    [Fact]
    public async Task CORS_stays_unconfigured_the_guard_refuses_origins_and_never_grants_one()
    {
        await using var web = new WebFactory();
        var response = await web.CreateClient().SendAsync(new HttpRequestMessage(HttpMethod.Options, "/") { Headers = { { "Origin", Foreign }, { "Access-Control-Request-Method", "POST" } } });
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        var allowed = await web.CreateClient().GetAsync("/health");
        Assert.False(allowed.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Behind_a_proxy_the_public_address_and_the_extra_origins_come_from_configuration()
    {
        await using var web = new WebFactory().With("Site:BaseUrl", "https://gabinety.example").With("AllowedOrigins", "https://www.gabinety.example");
        // The request arrives over plain http on the app's own host (the proxy terminated TLS): the browser's Origin is the public one.
        Assert.Equal(HttpStatusCode.OK, (await Send(web, HttpMethod.Post, "/_blazor/negotiate?negotiateVersion=1", ("Origin", "https://gabinety.example"))).Status);
        Assert.Equal(HttpStatusCode.OK, (await Send(web, HttpMethod.Post, "/_blazor/negotiate?negotiateVersion=1", ("Origin", "https://www.gabinety.example"))).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(web, HttpMethod.Post, "/_blazor/negotiate?negotiateVersion=1", ("Origin", "https://gabinety.example.evil.example"))).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(web, HttpMethod.Post, "/_blazor/negotiate?negotiateVersion=1", ("Origin", "http://gabinety.example"))).Status); // another scheme is another origin
    }
}
