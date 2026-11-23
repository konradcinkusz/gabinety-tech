using System.Text.RegularExpressions;

namespace Gabinety.Web.Tests.Pipeline;

/// <summary>
/// ADR-0010 deviates from BLAZOR-WEB-APP section 3 on purpose: static server rendering is the DEFAULT and InteractiveServer is applied per page. Section 3's pins ("Routes and HeadOutlet carry the
/// interactive render mode") are therefore reversed here, and what they protected is pinned another way: the interactive pages really are interactive, and the public pages really are not.
/// </summary>
public partial class RenderModeTests
{
    /// <summary>The pages that run a circuit. A page joins this list by a decision (ADR-0010), not by a stray directive: anonymous public pages never do.</summary>
    static readonly string[] InteractivePages = [];

    /// <summary>A page that is static and hosts ONE interactive component (a render mode on the component, not on the page): the checkout, whose clock ticks while its form posts stay plain.</summary>
    static readonly string[] PagesWithAnInteractivePart = ["Components/Pages/Booking/MockCheckout.razor"];

    [GeneratedRegex(@"@rendermode\b")]
    private static partial Regex RenderModeDirective();

    [Fact]
    public void The_roots_carry_no_render_mode_so_every_page_is_static_unless_it_asks()
    {
        var app = Repo.Read("Components", "App.razor");
        var routes = Repo.Read("Components", "Routes.razor");
        Assert.Matches(@"<HeadOutlet\s*/>", app); // present, and with no @rendermode on it
        Assert.Matches(@"<Routes\s*/>", app);
        Assert.DoesNotContain("@rendermode", app.Replace("NO @rendermode on Routes or HeadOutlet", "").Replace("@rendermode InteractiveServer itself", ""));
        Assert.DoesNotContain("RenderMode", routes);
        Assert.DoesNotContain("InteractiveServer", string.Join("\n", Repo.WebFiles("*.razor").Where(f => Repo.Rel(f) is "Components/App.razor" or "Components/Routes.razor" or "Components/Layout/MainLayout.razor").Select(File.ReadAllText)).Replace("@rendermode InteractiveServer itself", ""));
    }

    [Fact]
    public void Only_the_listed_pages_set_a_render_mode_and_each_sets_InteractiveServer()
    {
        var withMode = Repo.WebFiles("*.razor").Where(f => RenderModeDirective().IsMatch(StripComments(File.ReadAllText(f)))).Select(Repo.Rel).ToList();
        Assert.Equal([.. InteractivePages, .. PagesWithAnInteractivePart], withMode);
        foreach (var page in PagesWithAnInteractivePart) Assert.Matches(@"<HoldTimer @rendermode=""InteractiveServer""", Repo.Read(page.Split('/'))); // on the one component, never on the page itself
        foreach (var page in PagesWithAnInteractivePart) Assert.DoesNotMatch(@"(?m)^@rendermode", Repo.Read(page.Split('/')));
        foreach (var page in InteractivePages) Assert.Matches(@"(?m)^@rendermode InteractiveServer\s*$", Repo.Read(page.Split('/')));
    }

    [Fact]
    public void Public_pages_never_run_a_circuit()
    {
        foreach (var f in Repo.WebFiles("*.razor").Where(f => File.ReadAllText(f).Contains("@attribute [AllowAnonymous]")))
            Assert.DoesNotMatch(@"@rendermode|InteractiveServer|IJSRuntime", StripComments(File.ReadAllText(f)));
    }

    static string StripComments(string razor) => Regex.Replace(razor, @"@\*.*?\*@", "", RegexOptions.Singleline);

    [Fact]
    public void The_host_registers_interactive_server_components_and_the_render_mode_endpoint()
    {
        var hosting = string.Join("\n", Repo.WebFiles("*.cs").Where(f => Repo.Rel(f).StartsWith("Hosting/")).Select(File.ReadAllText));
        Assert.Contains("AddRazorComponents().AddInteractiveServerComponents(", hosting);
        Assert.Contains("MapRazorComponents<App>().AddInteractiveServerRenderMode()", hosting);
        Assert.DoesNotContain("WebAssembly", hosting + Repo.Read("Gabinety.Web.csproj"), StringComparison.OrdinalIgnoreCase); // Blazor WebAssembly puts the API address and the token in the browser: never
    }

    [Fact]
    public void The_framework_script_and_the_one_enhancement_script_are_loaded_once_from_the_app_not_a_cdn()
    {
        // UX-005 (owner decision D-1): exactly two scripts, both same-origin: the framework's and wwwroot/enhance.js (progressive enhancement, deferred)
        var app = Repo.Read("Components", "App.razor");
        Assert.Equal(2, Regex.Matches(app, @"<script\b").Count);
        Assert.Single(Regex.Matches(app, Regex.Escape("@Assets[\"_framework/blazor.web.js\"]")));
        Assert.Matches(@"<script src=""@Assets\[""enhance\.js""\]"" defer></script>", app);
        Assert.DoesNotMatch(@"https?://", app.Replace("<!DOCTYPE html>", ""));
    }

    [Fact]
    public async Task The_home_the_list_and_the_client_area_are_static_and_start_no_circuit()
    {
        await using var web = new WebFactory();
        web.Directory.Respond = r => r.RequestUri!.AbsolutePath switch
        {
            "/api/v1/taxonomy" => FakeBackend.Json(Sample.Taxonomy),
            "/api/v1/sitemap" => FakeBackend.Json(Sample.Sitemap()),
            _ => FakeBackend.Json(Sample.Cards(1)),
        };
        web.Booking.Respond = _ => FakeBackend.Json(new Gabinety.Web.Api.PageDto<Gabinety.Web.Api.BookingViewDto>([Sample.Booking("Konsultacja z prerenderu")], null));
        var browser = web.NewBrowser();

        foreach (var path in new[] { "/", "/programisci", "/programisci/audyt-kodu", "/logowanie" })
        {
            var page = await browser.GetAsync(path);
            Assert.DoesNotContain("<!--Blazor:", page.Html); // no component marker: no circuit will ever start for this page
            Assert.DoesNotContain("blazor-ssr-end", page.Html.Replace("_framework/blazor.web.js", ""));
        }

        await SignIn.As(web, browser);
        // The client area is static too: no circuit for a list of bookings, and its HTML is complete.
        var mine = await browser.GetAsync("/moje/rezerwacje");
        Assert.DoesNotContain("<!--Blazor:", mine.Html);
        Assert.Equal("Konsultacja z prerenderu", mine.TextOf("[data-testid=booking-row] .booking-row__title"));
    }
}
