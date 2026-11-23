using System.Net;
using Gabinety.Web.Text;

namespace Gabinety.Web.Tests.AdminPages;

/// <summary>F-10 and the Admin policy: a visitor goes to sign-in and comes back, a signed-in non-admin gets a real 403 page and no backend is called, an administrator (Admin or SuperAdmin) gets the page.</summary>
public class AdminAccessTests
{
    public static TheoryData<string> Routes => new()
    {
        "/admin", "/admin/weryfikacja", "/admin/weryfikacja?status=Approved", $"/admin/weryfikacja/{Guid.NewGuid()}", "/admin/spory", "/admin/opinie", "/admin/ksiega", "/admin/ksiega/csv?from=2026-12-01&to=2026-12-15", "/admin/podejrzane",
    };

    [Theory, MemberData(nameof(Routes))]
    public async Task A_visitor_is_sent_to_sign_in_with_the_page_to_come_back_to_and_nothing_is_called(string route)
    {
        await using var web = new WebFactory();
        var page = await web.NewBrowser().GetAsync(route);
        Assert.Equal(HttpStatusCode.Redirect, page.Status);
        Assert.StartsWith("/logowanie?redirect=", page.Location);
        Assert.Equal(route, Uri.UnescapeDataString(page.Location!["/logowanie?redirect=".Length..]));
        Assert.All([web.Directory, web.Booking, web.Reputation], b => Assert.Equal(0, b.CallsExceptShellCheck));
    }

    [Theory, MemberData(nameof(Routes))]
    public async Task A_signed_in_client_gets_a_real_403_page_and_no_backend_is_called(string route)
    {
        await using var host = await AdminHost.StartAsClientAsync();
        var page = await host.Browser.GetAsync(route);

        Assert.Equal(HttpStatusCode.Forbidden, page.Status); // a status, not a silent redirect to "/"
        Assert.Null(page.Location);
        var forbidden = page.One("[data-testid=forbidden]");
        Assert.NotNull(forbidden);
        Assert.Equal("alert", forbidden.GetAttribute("role"));
        Assert.Equal("Brak dostępu", Assert.Single(page.All("h1")).TextContent);
        Assert.Contains(PlAdmin.Forbidden.Body, page.Text);
        Assert.Equal("/", page.One("[data-testid=forbidden] a")!.GetAttribute("href"));
        Assert.Contains("noindex", page.One("meta[name=robots]")!.GetAttribute("content"));
        Assert.DoesNotContain("Weryfikacja programistów", page.TextOf("main")); // nothing of the area is rendered
        Assert.All([host.Web.Directory, host.Web.Booking, host.Web.Reputation], b => Assert.Equal(0, b.CallsExceptShellCheck));
    }

    [Fact]
    public async Task A_post_of_a_non_admin_is_refused_before_anything_acts()
    {
        await using var host = await AdminHost.StartAsClientAsync();
        var id = Guid.NewGuid();
        var answer = await host.PostRawAsync("/admin/weryfikacja", [KeyValuePair.Create("_handler", $"approve-{id}")]);
        Assert.Equal(HttpStatusCode.Forbidden, answer.Status);
        Assert.Equal(0, host.Web.Directory.CallsExceptShellCheck);
        Assert.Empty(host.Web.Errors);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("SuperAdmin")]
    public async Task An_administrator_or_a_super_administrator_gets_the_page(string role)
    {
        await using var host = await AdminHost.StartAsync([role]);
        host.Web.Directory.Respond = _ => AdminHost.Paged<Gabinety.Web.Api.AdminPractitionerRowDto>();
        var page = await host.Browser.GetAsync("/admin/weryfikacja");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Null(page.One("[data-testid=forbidden]"));
    }

    [Fact]
    public async Task A_user_with_several_roles_one_of_them_Admin_is_an_administrator()
    {
        await using var host = await AdminHost.StartAsync(["Client", "Admin"]);
        host.Web.Directory.Respond = _ => AdminHost.Paged<Gabinety.Web.Api.AdminPractitionerRowDto>();
        Assert.Equal(HttpStatusCode.OK, (await host.Browser.GetAsync("/admin/weryfikacja")).Status);
    }

    [Fact]
    public async Task The_header_offers_Administracja_to_an_administrator_and_to_nobody_else()
    {
        await using var admin = await AdminHost.StartAsync();
        Assert.Contains(("/admin/weryfikacja", "Administracja"), (await admin.Browser.GetAsync("/")).All("header nav a").Select(a => (a.GetAttribute("href")!, a.TextContent.Trim())));

        await using var client = await AdminHost.StartAsClientAsync();
        Assert.DoesNotContain("/admin/weryfikacja", (await client.Browser.GetAsync("/")).All("header nav a").Select(a => a.GetAttribute("href")));
        await using var visitor = new WebFactory();
        Assert.DoesNotContain("/admin/weryfikacja", (await visitor.NewBrowser().GetAsync("/")).All("header nav a").Select(a => a.GetAttribute("href")));
    }

    [Fact]
    public async Task The_area_has_one_h1_a_labelled_tab_strip_with_the_current_section_marked_and_is_not_indexed()
    {
        await using var host = await AdminHost.StartAsync();
        host.Web.Booking.Respond = _ => AdminHost.Paged<Gabinety.Web.Api.AdminDisputeDto>();
        var page = await host.Browser.GetAsync("/admin/spory");
        Assert.Single(page.All("h1"));
        var tabs = page.All("nav[aria-label='Sekcje administracji'] a");
        Assert.Equal(["Weryfikacja programistów", "Spory", "Księga", "Opinie", "Podejrzane pary"], tabs.Select(a => a.TextContent.Trim()));
        Assert.Equal(["Spory"], tabs.Where(a => a.GetAttribute("aria-current") == "page").Select(a => a.TextContent.Trim()));
        Assert.Contains("noindex", page.One("meta[name=robots]")!.GetAttribute("content"));
        Assert.Single(page.All("nav"), n => n.GetAttribute("aria-label") == "Sekcje administracji"); // the area's own navs have names of their own (contract 7.5)
        Assert.Single(page.All("nav"), n => n.GetAttribute("aria-label") == "Status");
    }

    [Fact]
    public async Task The_landing_links_to_every_section_and_calls_nobody()
    {
        await using var host = await AdminHost.StartAsync();
        var page = await host.Browser.GetAsync("/admin");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal("Administracja", Assert.Single(page.All("h1")).TextContent);
        Assert.Equal(["/admin/weryfikacja", "/admin/spory", "/admin/ksiega", "/admin/opinie", "/admin/podejrzane"], page.All("main ul[aria-label] h2 a").Select(a => a.GetAttribute("href")));
        Assert.All([host.Web.Directory, host.Web.Booking, host.Web.Reputation], b => Assert.Equal(0, b.CallsExceptShellCheck));
    }

    [Fact]
    public async Task A_backend_that_refuses_the_role_is_shown_as_403_too_the_service_is_the_boundary()
    {
        await using var host = await AdminHost.StartAsync(); // the cookie says Admin, the service says no (a role that was withdrawn)
        host.Web.Directory.Respond = _ => FakeBackend.Problem(HttpStatusCode.Forbidden, "forbidden");
        var page = await host.Browser.GetAsync($"/admin/weryfikacja/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Forbidden, page.Status);
        Assert.NotNull(page.One("[data-testid=forbidden]"));
    }

    [Fact]
    public async Task The_foundations_404_page_is_still_a_404_page_and_not_the_forbidden_one()
    {
        await using var web = new WebFactory();
        var page = await web.NewBrowser().GetAsync("/no/such/page");
        Assert.Equal(HttpStatusCode.NotFound, page.Status);
        Assert.Null(page.One("[data-testid=forbidden]"));
        Assert.Equal("Nie znaleziono strony", page.TextOf("h1"));
    }
}
