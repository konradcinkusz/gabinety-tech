using System.Net;
using System.Text.Json;
using Gabinety.Web.Api;
using Gabinety.Web.Tests.Pages;

namespace Gabinety.Web.Tests.Pipeline;

/// <summary>UX-004: the document head of a public page (PageMeta and App.razor) and the brand files it points at. Every reference is same-origin and must be served by the host, because the CSP is img-src 'self' data:.</summary>
public class DocumentHeadTests
{
    static WebFactory Backends()
    {
        var web = new WebFactory();
        web.Directory.Respond = r => r.RequestUri!.AbsolutePath switch
        {
            "/api/v1/taxonomy" => FakeBackend.Json(Sample.Taxonomy),
            "/api/v1/sitemap" => FakeBackend.Json(Sample.Sitemap(6, true)),
            "/api/v1/practitioners" => FakeBackend.Json(Sample.Cards(2, "NEXT")),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };
        return web;
    }

    [Fact]
    public async Task A_public_page_carries_the_default_social_image_and_the_large_summary_card()
    {
        await using var web = Backends();
        var page = await web.NewBrowser().GetAsync("/");
        Assert.Equal("http://localhost/og-image.png", page.One("meta[property='og:image']")!.GetAttribute("content"));
        Assert.Equal(("1200", "630"), (page.One("meta[property='og:image:width']")!.GetAttribute("content"), page.One("meta[property='og:image:height']")!.GetAttribute("content")));
        Assert.Equal("summary_large_image", page.One("meta[name='twitter:card']")!.GetAttribute("content"));
        Assert.Equal("Gabinety Tech", page.One("meta[property='og:image:alt']")!.GetAttribute("content"));
    }

    [Fact]
    public async Task The_social_image_is_the_same_site_address_on_a_profile_page_of_another_path()
    {
        await using var web = new WebFactory();
        web.Directory.Respond = r => r.RequestUri!.AbsolutePath switch
        {
            "/api/v1/taxonomy" => FakeBackend.Json(Sample.Taxonomy),
            "/api/v1/practitioners/demo-anna-demowska" => FakeBackend.Json(ClientAreaFixture.Profile()),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };
        var page = await web.NewBrowser().GetAsync("/gabinet/demo-anna-demowska");
        Assert.Equal("http://localhost/gabinet/demo-anna-demowska", page.One("link[rel=canonical]")!.GetAttribute("href"));
        Assert.Equal("http://localhost/og-image.png", page.One("meta[property='og:image']")!.GetAttribute("content"));
        Assert.Equal("summary_large_image", page.One("meta[name='twitter:card']")!.GetAttribute("content"));
    }

    [Fact]
    public async Task The_head_links_the_icons_the_manifest_and_a_theme_colour_for_each_scheme_and_each_one_is_served()
    {
        await using var web = Backends();
        var browser = web.NewBrowser();
        var page = await browser.GetAsync("/");

        // @Assets writes paths relative to <base href="/">; a fingerprint may be added in publish, so the names are matched, not the hash.
        static string Root(string? href) => "/" + href!.TrimStart('/');
        Assert.Matches(@"^/favicon(\.[\w]+)?\.svg$", Root(page.All("link[rel='icon']")[0].GetAttribute("href")));
        Assert.Matches(@"^/favicon-32(\.[\w]+)?\.png$", Root(page.All("link[rel='icon']")[1].GetAttribute("href")));
        Assert.Matches(@"^/apple-touch-icon(\.[\w]+)?\.png$", Root(page.One("link[rel='apple-touch-icon']")!.GetAttribute("href")));
        Assert.Matches(@"^/site(\.[\w]+)?\.webmanifest$", Root(page.One("link[rel='manifest']")!.GetAttribute("href")));
        Assert.Equal(["(prefers-color-scheme: light)", "(prefers-color-scheme: dark)"], page.All("meta[name='theme-color']").Select(m => m.GetAttribute("media")));
        Assert.All(page.All("meta[name='theme-color']"), m => Assert.Matches("^#[0-9a-f]{6}$", m.GetAttribute("content")!));

        foreach (var href in page.All("link[rel='icon'], link[rel='apple-touch-icon'], link[rel='manifest']").Select(l => Root(l.GetAttribute("href"))))
        {
            var served = await browser.GetAsync(href);
            Assert.True(served.Status == HttpStatusCode.OK, $"{href} is not served ({(int)served.Status})");
        }
    }

    [Fact]
    public async Task The_manifest_is_the_web_app_manifest_of_the_site_and_each_of_its_icons_is_a_file_in_wwwroot()
    {
        await using var web = Backends();
        var manifest = await web.NewBrowser().GetAsync("/site.webmanifest");
        Assert.Equal(HttpStatusCode.OK, manifest.Status);
        Assert.Equal("application/manifest+json", manifest.Response.Content.Headers.ContentType?.MediaType);

        using var doc = JsonDocument.Parse(manifest.Html);
        var root = doc.RootElement;
        Assert.Equal(("Gabinety Tech", "pl", "standalone"), (root.GetProperty("name").GetString(), root.GetProperty("lang").GetString(), root.GetProperty("display").GetString()));
        Assert.Equal("/", root.GetProperty("start_url").GetString());

        var icons = root.GetProperty("icons").EnumerateArray().ToList();
        Assert.Contains(icons, i => i.GetProperty("sizes").GetString() == "192x192");
        Assert.Contains(icons, i => i.GetProperty("sizes").GetString() == "512x512");
        foreach (var icon in icons)
        {
            var src = icon.GetProperty("src").GetString()!;
            Assert.StartsWith("/", src); // same origin, absolute from the root
            Assert.True(File.Exists(Path.Combine(Repo.Web, "wwwroot", src.TrimStart('/'))), $"{src} is listed in the manifest but is not in wwwroot");
        }
    }

    [Fact]
    public void The_social_image_file_is_a_1200_by_630_png()
    {
        // The PNG signature, then the IHDR chunk: width and height are big-endian 32-bit integers at offsets 16 and 20.
        var bytes = File.ReadAllBytes(Path.Combine(Repo.Web, "wwwroot", "og-image.png"));
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, bytes[..4]);
        Assert.Equal(1200, (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19]);
        Assert.Equal(630, (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23]);
    }

    [Fact]
    public void App_razor_names_each_brand_file_through_Assets_and_a_theme_colour_for_each_scheme()
    {
        var app = Repo.Read("Components", "App.razor");
        foreach (var file in new[] { "favicon.svg", "favicon-32.png", "apple-touch-icon.png", "site.webmanifest" })
            Assert.Contains($"@Assets[\"{file}\"]", app);
        Assert.Contains("media=\"(prefers-color-scheme: dark)\"", app);
        Assert.Contains("media=\"(prefers-color-scheme: light)\"", app);
    }
}
