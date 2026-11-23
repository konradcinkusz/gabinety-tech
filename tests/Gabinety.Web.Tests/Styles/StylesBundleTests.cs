using System.Text.RegularExpressions;

namespace Gabinety.Web.Tests.Stylesheets;

/// <summary>BLAZOR-WEB-APP section 3 and 15: each Foo.razor.css compiles into Gabinety.Web.styles.css, which App.razor must link, or every scoped rule is silently absent and the app renders unstyled.</summary>
public partial class StylesBundleTests
{
    [GeneratedRegex(@"<link\s+rel=""stylesheet""\s+href=""([^""]+)""")] private static partial Regex StylesheetLink();

    [Fact]
    public void App_razor_links_the_scoped_bundle_and_the_two_sheets_in_order()
    {
        var app = Repo.Read("Components", "App.razor");
        var order = new[] { "@Assets[\"tokens.css\"]", "@Assets[\"app.css\"]", "@Assets[\"Gabinety.Web.styles.css\"]" }.Select(l => app.IndexOf(l, StringComparison.Ordinal)).ToArray();
        Assert.All(order, i => Assert.True(i >= 0, "App.razor must link tokens.css, app.css and Gabinety.Web.styles.css with @Assets[...]"));
        Assert.Equal(order.Order(), order); // tokens, then shared classes, then the scoped bundle
        Assert.True(app.IndexOf("<HeadOutlet", StringComparison.Ordinal) > order[2]);
    }

    [Fact]
    public void The_bundle_exists_when_a_component_has_scoped_css()
    {
        Assert.NotEmpty(Repo.WebFiles("*.razor.css")); // the link above would point at nothing otherwise
        foreach (var css in Repo.WebFiles("*.razor.css")) Assert.True(File.Exists(css[..^4]), $"{Repo.Rel(css)} has no component beside it");
    }

    [Fact]
    public async Task The_page_links_a_bundle_the_host_serves_and_it_holds_the_scoped_rules_of_the_markup()
    {
        await using var web = new WebFactory();
        web.Directory.Respond = _ => FakeBackend.Json(Sample.Taxonomy);
        var browser = web.NewBrowser();
        var home = await browser.GetAsync("/");

        var hrefs = StylesheetLink().Matches(home.Html).Select(m => "/" + m.Groups[1].Value.TrimStart('/')).ToList(); // relative to <base href="/">
        Assert.Equal(3, hrefs.Count);
        Assert.Matches(@"^/tokens(\.[\w]+)?\.css$", hrefs[0]);
        Assert.Matches(@"^/app(\.[\w]+)?\.css$", hrefs[1]);
        Assert.Matches(@"^/Gabinety\.Web(\.[\w]+)?\.styles\.css$", hrefs[2]);

        var bundle = await browser.GetAsync(hrefs[2]);
        Assert.Equal("text/css", bundle.Response.Content.Headers.ContentType?.MediaType);
        // the scope id the markup carries is the one the bundle's rules select
        var scope = Regex.Match(home.Html, @"<header class=""site-header"" (b-\w+)").Groups[1].Value;
        Assert.NotEmpty(scope);
        Assert.Contains($".site-header[{scope}]", bundle.Html);
        Assert.Contains($".nav-list[{scope}]", bundle.Html);

        Assert.Equal("text/css", (await browser.GetAsync(hrefs[1])).Response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("--color-bg", (await browser.GetAsync(hrefs[0])).Html);
    }

    [Fact]
    public async Task A_page_of_the_list_gets_its_own_scoped_rules_too()
    {
        await using var web = new WebFactory();
        web.Directory.Respond = r => r.RequestUri!.AbsolutePath switch
        {
            "/api/v1/taxonomy" => FakeBackend.Json(Sample.Taxonomy),
            "/api/v1/sitemap" => FakeBackend.Json(Sample.Sitemap()),
            _ => FakeBackend.Json(Sample.Cards(1)),
        };
        var browser = web.NewBrowser();
        var list = await browser.GetAsync("/programisci");
        var bundle = await browser.GetAsync("/" + StylesheetLink().Matches(list.Html).Last().Groups[1].Value.TrimStart('/'));
        var cardScope = Regex.Match(list.Html, @"<article class=""card card--link practitioner-card"" data-testid=""practitioner-card"" (b-\w+)").Groups[1].Value;
        Assert.NotEmpty(cardScope);
        Assert.Contains($".practitioner-card__name[{cardScope}]", bundle.Html);
    }
}
