using System.Net;
using System.Text.RegularExpressions;

namespace Gabinety.Web.Tests.Pipeline;

/// <summary>The policy intent of PR #113 (web/src/lib/security/headers.ts) on the Blazor host. What differs from the Next.js policy is stated in <c>SecurityHeaders</c> and in ADR-0010.</summary>
public class SecurityHeadersTests
{
    static async Task<Page> Home(WebFactory web)
    {
        web.Directory.Respond = _ => FakeBackend.Json(Sample.Taxonomy);
        return await web.NewBrowser().GetAsync("/");
    }

    static string Csp(Page p) => p.Header("Content-Security-Policy")!;

    [Fact]
    public async Task The_CSP_allows_only_the_apps_own_script_and_never_inline_or_eval()
    {
        await using var web = new WebFactory();
        var csp = Csp(await Home(web));
        var directives = csp.Split("; ").ToDictionary(d => d.Split(' ')[0], d => d);
        Assert.Equal("script-src 'self'", directives["script-src"]);
        Assert.Equal("style-src 'self'", directives["style-src"]);
        Assert.DoesNotContain("unsafe-inline", csp);
        Assert.DoesNotContain("unsafe-eval", csp);
        Assert.DoesNotContain("strict-dynamic", csp);
        Assert.DoesNotContain("*", csp);
        Assert.DoesNotContain("https:", csp.Replace("wss:", ""));
        Assert.Equal("default-src 'self'", directives["default-src"]);
        Assert.Equal("frame-ancestors 'none'", directives["frame-ancestors"]);
        Assert.Equal("object-src 'none'", directives["object-src"]);
        Assert.Equal("base-uri 'self'", directives["base-uri"]);
        Assert.Equal("form-action 'self'", directives["form-action"]);
        Assert.Equal("img-src 'self' data:", directives["img-src"]);
        Assert.Equal("font-src 'self'", directives["font-src"]);
    }

    [Fact]
    public async Task The_circuit_websocket_is_allowed_to_this_host_and_to_nothing_else()
    {
        await using var web = new WebFactory();
        var page = await Home(web);
        Assert.Equal("connect-src 'self' ws://localhost", Csp(page).Split("; ").Single(d => d.StartsWith("connect-src")));
        await using var tls = new WebFactory();
        var response = await tls.CreateClient(new() { BaseAddress = new Uri("https://gabinety.example") }).GetAsync("/health");
        Assert.Equal("connect-src 'self' wss://gabinety.example", response.Headers.GetValues("Content-Security-Policy").Single().Split("; ").Single(d => d.StartsWith("connect-src")));
    }

    [Fact]
    public async Task The_other_headers_are_those_of_the_nextjs_app()
    {
        await using var web = new WebFactory();
        var page = await Home(web);
        Assert.Equal("nosniff", page.Header("X-Content-Type-Options"));
        Assert.Equal("strict-origin-when-cross-origin", page.Header("Referrer-Policy"));
        Assert.Equal("DENY", page.Header("X-Frame-Options"));
        var permissions = page.Header("Permissions-Policy")!;
        foreach (var feature in new[] { "camera", "microphone", "geolocation", "payment", "usb", "interest-cohort", "browsing-topics" }) Assert.Contains($"{feature}=()", permissions);
        Assert.Null(page.Header("Strict-Transport-Security")); // plain http: HSTS would pin localhost to https
    }

    [Fact]
    public async Task HSTS_is_sent_only_where_the_site_is_served_over_https()
    {
        await using var web = new WebFactory().With("Site:BaseUrl", "https://gabinety.example");
        Assert.Equal("max-age=63072000; includeSubDomains", (await Home(web)).Header("Strict-Transport-Security"));
    }

    [Fact]
    public async Task Every_kind_of_response_carries_the_headers_pages_assets_probes_refusals_and_redirects()
    {
        await using var web = new WebFactory();
        web.Directory.Respond = _ => FakeBackend.Json(Sample.Taxonomy);
        var browser = web.NewBrowser();
        var styles = Regex.Match((await browser.GetAsync("/")).Html, "href=\"([^\"]*styles[^\"]*\\.css)\"").Groups[1].Value;
        foreach (var path in new[] { "/", "/programisci", "/nie-ma-takiej", "/health", "/alive", "/moje/rezerwacje", styles, "/_framework/blazor.web.js" })
        {
            var page = await browser.GetAsync(path);
            Assert.True(page.Header("Content-Security-Policy") is { Length: > 0 } && page.Header("X-Content-Type-Options") == "nosniff", $"{path} → {(int)page.Status}");
        }
        var refused = await browser.GetAsync("/", ("Origin", "https://evil.example"));
        Assert.Equal(HttpStatusCode.Forbidden, refused.Status);
        Assert.Equal("nosniff", refused.Header("X-Content-Type-Options"));
    }

    [Fact]
    public void No_page_or_component_has_an_inline_script_or_style_that_the_policy_would_block()
    {
        foreach (var f in Repo.WebFiles("*.razor"))
        {
            var razor = File.ReadAllText(f);
            Assert.DoesNotMatch(@"<style\b", razor);
            Assert.DoesNotMatch(@"\sstyle\s*=", razor); // an inline style attribute is blocked by style-src 'self'
            Assert.DoesNotMatch(@"\son[a-z]+\s*=\s*""", razor.Replace("@onclick", "").Replace("@onchange", "")); // no inline event handler attribute
            // A JSON-LD block is data, not code: the browser never executes it and script-src does not apply (the profile page needs one, journey J1). Any other script is refused.
            if (Repo.Rel(f) != "Components/App.razor") Assert.DoesNotMatch(@"<script\b(?![^>]*type=""application/ld\+json"")", razor);
        }
    }
}
